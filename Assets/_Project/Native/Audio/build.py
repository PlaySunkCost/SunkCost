"""Build pinned source with Zig 0.13.0: python build.py <zig.exe> [windows|linux].
Run from repository root. Sources are fetched to ignored Library/VoiceBuild/deps.
"""
import pathlib, subprocess, sys, re, os
root=pathlib.Path.cwd()
deps=root/'Library/VoiceBuild/deps'
deps.mkdir(parents=True,exist_ok=True)
(root/'Library/VoiceBuild/tools').mkdir(parents=True,exist_ok=True)
pins={'miniaudio':('https://github.com/mackron/miniaudio.git','4a5b74bef029b3592c54b6048650ee5f972c1a48'),
      'opus':('https://github.com/xiph/opus.git','ddbe48383984d56acd9e1ab6a090c54ca6b735a6'),
      'unityaudio':('https://github.com/Unity-Technologies/NativeAudioPlugins.git','bc7893edbba4c8a592777e590e34f21a11d762b4')}
for name,(url,rev) in pins.items():
    path=deps/name
    if not path.exists():
        subprocess.run(['git','clone',url,str(path)],check=True)
    actual=subprocess.check_output(['git','-C',str(path),'rev-parse','HEAD'],text=True).strip()
    if actual!=rev: subprocess.run(['git','-C',str(path),'checkout','--detach',rev],check=True)
    if subprocess.check_output(['git','-C',str(path),'status','--porcelain'],text=True).strip():
        raise RuntimeError('Dependency checkout is modified: '+str(path))
opus=deps/'opus'
sources=[]
for file,keys in [('opus_sources.mk',['OPUS_SOURCES','OPUS_SOURCES_FLOAT']),('celt_sources.mk',['CELT_SOURCES']),('silk_sources.mk',['SILK_SOURCES','SILK_SOURCES_FLOAT'])]:
    text=(opus/file).read_text().replace('\\\n',' ')
    for key in keys:
        match=re.search(r'^'+key+r'\s*=([^\n]*)',text,re.M)
        if not match: raise RuntimeError('Missing '+key)
        sources.extend(str(opus/x) for x in match.group(1).split())
platform=sys.argv[2] if len(sys.argv)>2 else 'windows'
if platform not in ('windows','linux'): raise ValueError('Expected windows or linux')
folder=root/'Assets/_Project/Plugins/Audio'/('Windows' if platform=='windows' else 'Linux')
folder.mkdir(parents=True,exist_ok=True)
target='x86_64-windows-gnu' if platform=='windows' else 'x86_64-linux-gnu'
dest=folder/('AudioPluginSunkCostAudio.dll' if platform=='windows' else 'libAudioPluginSunkCostAudio.so')
env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(root/'Library/VoiceBuild/tools/cache')
obj=root/'Library/VoiceBuild/tools'/('mixer-'+platform+'.o')
subprocess.run([sys.argv[1],'c++','-target',target,'-c','-O2','-fPIC','-std=c++17','-I'+str(deps/'unityaudio/NativeCode'),str(root/'Assets/_Project/Native/Audio/mixer_route.cpp'),'-o',str(obj)],check=True,env=env)
args=['cc','-target',target,'-shared','-O2','-std=c11','-DOPUS_BUILD','-DUSE_ALLOCA','-DHAVE_LRINTF','-DHAVE_LRINT',
      '-I'+str(deps/'miniaudio'),'-I'+str(opus/'include'),'-I'+str(opus),'-I'+str(opus/'celt'),'-I'+str(opus/'silk'),'-I'+str(opus/'silk/float'),
      str(root/'Assets/_Project/Native/Audio/voice_audio.c'),*sources,str(obj),'-o',str(dest)]
if platform=='windows': args+=['-lole32','-luuid','-lwinmm']
else: args+=['-DHAVE_ALLOCA_H','-fPIC','-lpthread','-ldl','-lm']
env=os.environ.copy();env['ZIG_GLOBAL_CACHE_DIR']=str(root/'Library/VoiceBuild/tools/cache')
response=root/'Library/VoiceBuild/tools'/('build-'+platform+'.rsp')
response.write_text('\n'.join('"'+a.replace('\\','/')+'"' for a in args[1:]))
subprocess.run([sys.argv[1],'cc','@'+str(response)],check=True,env=env)
# Linker debug/import byproducts are not runtime assets.
for extension in ('.pdb','.lib'):
    for extra in folder.glob('*'+extension): extra.unlink()
print(dest)
