/* First-party C ABI. Device callbacks use bounded, allocation-free SPSC rings. */
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include "opus.h"
#include <stdatomic.h>
#include <stdio.h>
#include <math.h>
#if defined(_WIN32)
#define API __declspec(dllexport)
#else
#define API __attribute__((visibility("default")))
#endif
#define CAP 65536
typedef struct { float samples[CAP]; atomic_uint read,write; } Ring;
static Ring input,output;
static ma_context context;
static ma_device capture,playback;
static int initialized,capture_open,playback_open;
static atomic_int capturing,playing;
static atomic_uint output_writers;
static atomic_uint output_callbacks,output_nonzero;
static int output_primed;
static ma_device_info inputs[64],outputs[64];
static unsigned input_count,output_count;
static unsigned available(Ring* r) { return atomic_load_explicit(&r->write,memory_order_acquire)-atomic_load_explicit(&r->read,memory_order_acquire); }
static unsigned push(Ring* r,const float* data,unsigned n) {
    unsigned w=atomic_load(&r->write), rd=atomic_load_explicit(&r->read,memory_order_acquire);
    if(n>CAP-(w-rd)) return 0; /* Drop new data on overflow, never overwrite reader. */
    for(unsigned i=0;i<n;i++) r->samples[(w+i)%CAP]=data[i];
    atomic_store_explicit(&r->write,w+n,memory_order_release); return n;
}
static unsigned pop(Ring* r,float* data,unsigned n) {
    unsigned rd=atomic_load(&r->read),w=atomic_load_explicit(&r->write,memory_order_acquire);
    if(n>w-rd) n=w-rd;
    for(unsigned i=0;i<n;i++) data[i]=r->samples[(rd+i)%CAP];
    atomic_store_explicit(&r->read,rd+n,memory_order_release); return n;
}
static void capture_callback(ma_device* d,void* out,const void* in,ma_uint32 frames) {
    (void)d;(void)out; if(atomic_load(&capturing)&&in) push(&input,(const float*)in,frames);
}
static void output_callback(ma_device* d,void* out,const void* in,ma_uint32 frames) {
    (void)in; float* p=out; memset(p,0,frames*2*sizeof(float));
    if(!atomic_load(&playing)) return;
    /* Drain latency spikes, then very gently match the independent DSP clock. */
    unsigned count=available(&output), target=d->sampleRate/20*2;
    if(!output_primed) { if(count<target)return; output_primed=1; }
    if(count>d->sampleRate/5*2) {
        unsigned rd=atomic_load(&output.read); atomic_store(&output.read,rd+((count-target)&~1u)); count=available(&output);
    }
    unsigned need=frames*2;
    unsigned consumed=pop(&output,p,need);
    if(consumed<need) output_primed=0;
    if(count>target+4096 && available(&output)>=2) { float skip[2]; pop(&output,skip,2); }
    atomic_fetch_add(&output_callbacks,1);
    for(unsigned i=0;i<consumed;i++) if(fabsf(p[i])>0.00001f) { atomic_fetch_add(&output_nonzero,1); break; }
}
API int sc_init(void) {
    if(initialized) return 0;
    if(ma_context_init(NULL,0,NULL,&context)!=MA_SUCCESS) return -1;
    initialized=1; return 0;
}
API int sc_refresh(void) {
    if(sc_init()!=0) return -1;
    ma_device_info *p,*c; ma_uint32 np,nc;
    if(ma_context_get_devices(&context,&p,&np,&c,&nc)!=MA_SUCCESS) return -1;
    output_count=np>64?64:np; input_count=nc>64?64:nc;
    memcpy(outputs,p,output_count*sizeof(*p)); memcpy(inputs,c,input_count*sizeof(*c)); return 0;
}
API int sc_count(int mic) { return mic?(int)input_count:(int)output_count; }
API const char* sc_name(int mic,int index) {
    if(index<0||index>=sc_count(mic)) return ""; return (mic?inputs:outputs)[index].name;
}
/* Stable backend identifier, independent of display name and enumeration order. */
API void sc_id(int mic,int index,char* text,int capacity) {
    if(capacity<1) return; text[0]=0;
    if(index<0||index>=sc_count(mic)) return;
    ma_device_id* id=&(mic?inputs:outputs)[index].id;
    const unsigned char* bytes=(const unsigned char*)id;
    unsigned n=sizeof(*id);
    if(context.backend==ma_backend_wasapi) { n=0; while(id->wasapi[n]) ++n; n*=sizeof(id->wasapi[0]); }
    else if(context.backend==ma_backend_pulseaudio) n=(unsigned)strlen(id->pulse);
    else if(context.backend==ma_backend_alsa) n=(unsigned)strlen(id->alsa);
    unsigned long long hash=1469598103934665603ull;
    for(unsigned i=0;i<n;i++) { hash^=bytes[i]; hash*=1099511628211ull; }
    snprintf(text,capacity,"%d-%016llx",(int)context.backend,hash);
}
API int sc_default(int mic,int index) { if(index<0||index>=sc_count(mic))return 0; return (mic?inputs:outputs)[index].isDefault; }
API void sc_capture_stop(void) {
    atomic_store(&capturing,0); if(capture_open) ma_device_uninit(&capture); capture_open=0;
    atomic_store(&input.read,0); atomic_store(&input.write,0);
}
API int sc_capture_start(int index) {
    sc_capture_stop(); if(!initialized||index>=sc_count(1))return -1;
    ma_device_config cfg=ma_device_config_init(ma_device_type_capture);
    cfg.capture.format=ma_format_f32; cfg.capture.channels=1; cfg.sampleRate=48000;
    cfg.capture.pDeviceID=index<0?NULL:&inputs[index].id; cfg.dataCallback=capture_callback;
    if(ma_device_init(&context,&cfg,&capture)!=MA_SUCCESS)return -2;
    capture_open=1; atomic_store(&capturing,1);
    if(ma_device_start(&capture)!=MA_SUCCESS) { sc_capture_stop(); return -3; } return 0;
}
API int sc_capture_read(float* data,int count) { if(count<0||count>CAP)return 0; return (int)pop(&input,data,(unsigned)count); }
API int sc_capture_available(void) { return (int)available(&input); }
API void sc_output_stop(void) {
    atomic_store(&playing,0); if(playback_open)ma_device_uninit(&playback); playback_open=0;
    while(atomic_load(&output_writers)) ma_sleep(1);
    /* Ring storage is static; concurrent Unity writes are safe, discarded on next open. */
}
API int sc_output_start(int index,int sample_rate) {
    sc_output_stop(); if(!initialized||index>=sc_count(0)||sample_rate<8000||sample_rate>192000)return -1;
    /* Unity producer must be paused by managed routing before calling this. */
    atomic_store(&output.read,0); atomic_store(&output.write,0);
    output_primed=0;
    ma_device_config cfg=ma_device_config_init(ma_device_type_playback);
    cfg.playback.format=ma_format_f32; cfg.playback.channels=2; cfg.sampleRate=sample_rate;
    cfg.playback.pDeviceID=index<0?NULL:&outputs[index].id; cfg.dataCallback=output_callback;
    if(ma_device_init(&context,&cfg,&playback)!=MA_SUCCESS)return -2;
    playback_open=1; atomic_store(&playing,1);
    if(ma_device_start(&playback)!=MA_SUCCESS){sc_output_stop();return -3;}return 0;
}
API int sc_output_write(const float* data,int samples) {
    atomic_fetch_add(&output_writers,1);
    int count=0;
    if(atomic_load(&playing)&&samples>=0&&samples<=CAP) count=(int)push(&output,data,(unsigned)samples);
    atomic_fetch_sub(&output_writers,1); return count;
}
API unsigned sc_output_callbacks(void) { return atomic_load(&output_callbacks); }
API unsigned sc_output_nonzero(void) { return atomic_load(&output_nonzero); }
API int sc_output_active(void) { return atomic_load(&playing); }
API void sc_shutdown(void) { sc_capture_stop();sc_output_stop();if(initialized)ma_context_uninit(&context);initialized=0; }
API void* sc_encoder_create(void) {
    int err=0;OpusEncoder* e=opus_encoder_create(48000,1,OPUS_APPLICATION_VOIP,&err);if(err)return NULL;
    opus_encoder_ctl(e,OPUS_SET_BITRATE(24000));opus_encoder_ctl(e,OPUS_SET_VBR_CONSTRAINT(1));return e;
}
API void sc_encoder_destroy(void* e){if(e)opus_encoder_destroy(e);}
API int sc_encode(void* e,const float* pcm,unsigned char* bytes){return e?opus_encode_float(e,pcm,960,bytes,400):-1;}
API void* sc_decoder_create(void){int err=0;OpusDecoder* d=opus_decoder_create(48000,1,&err);return err?NULL:d;}
API void sc_decoder_destroy(void* d){if(d)opus_decoder_destroy(d);}
API int sc_decode(void* d,const unsigned char* bytes,int size,float* pcm){if(!d||size<0||size>400)return -1;return opus_decode_float(d,bytes,size,pcm,960,0);}
