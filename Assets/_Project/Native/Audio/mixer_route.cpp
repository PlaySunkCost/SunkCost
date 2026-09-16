#include "AudioPluginInterface.h"
#include <atomic>
#include <cstring>
#include <cmath>
extern "C" int sc_output_write(const float*,int);
extern "C" int sc_output_active(void);
static std::atomic<float> master(1),peak(0);
static std::atomic<unsigned> callbacks(0);
extern "C" UNITY_AUDIODSP_EXPORT_API void sc_master(float value) { master.store(value); }
extern "C" UNITY_AUDIODSP_EXPORT_API float sc_output_peak() { return peak.load(); }
extern "C" UNITY_AUDIODSP_EXPORT_API unsigned sc_mix_callbacks() { return callbacks.load(); }
static UNITY_AUDIODSP_RESULT UNITY_AUDIODSP_CALLBACK Process(UnityAudioEffectState* state,float* in,float* out,unsigned frames,int channels,int outchannels)
{
    if(channels!=outchannels) { std::memset(out,0,frames*outchannels*sizeof(float)); return UNITY_AUDIODSP_OK; }
    float gain=master.load(),level=0;
    for(unsigned i=0;i<frames*channels;i++) { out[i]=in[i]*gain; level=std::fmax(level,std::fabs(out[i])); }
    peak.store(level); callbacks.fetch_add(1);
    if(channels==2 && (state->flags&UnityAudioEffectStateFlags_IsPlaying) && sc_output_active()) {
        sc_output_write(out,frames*2);
        std::memset(out,0,frames*2*sizeof(float));
    }
    return UNITY_AUDIODSP_OK;
}
extern "C" UNITY_AUDIODSP_EXPORT_API int AUDIO_CALLING_CONVENTION UnityGetAudioEffectDefinitions(UnityAudioEffectDefinition*** result)
{
    static UnityAudioEffectDefinition definition = {};
    static UnityAudioEffectDefinition* definitions[] = { &definition };
    definition.structsize=sizeof(definition); definition.paramstructsize=sizeof(UnityAudioParameterDefinition);
    definition.apiversion=UNITY_AUDIO_PLUGIN_API_VERSION; definition.pluginversion=1;
    std::strcpy(definition.name,"Sunk Cost Output"); definition.process=Process;
    *result=definitions; return 1;
}
