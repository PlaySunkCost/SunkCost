"""Prepare the approved HQ kit. Blender -b -t 4 -P tools/blender/prepare_hq.py.

Input: Library/HQBuild/archives.json (extracted user downloads). Originals are
untouched. Dimensions below are Blender X/Y/Z; export bakes Unity Y-up axes.
Rebakes colour/normals onto a reduced mesh rather than collapsing the old atlas.
"""
import bpy, json, pathlib, sys, math, hashlib
from mathutils import Vector

ROOT = pathlib.Path(__file__).resolve().parents[2]
helpers = (ROOT / 'tools/blender/prepare_ship_part.py').read_text()
exec(helpers.split('# ---- the run')[0])
# index, name, width/depth/height, triangle budget, quarter-turns around up.
PARTS = [
 (0,'IntakeHopper',(2,1.5,1.1),7000,0),
 (1,'Girder',(4,.4,.8),2500,0),
 (2,'Fascia',(4,.25,1),2500,0),
 (3,'Pallet',(1.2,1,.16),2000,0),
 (4,'TankCradle',(.5,.35,1),4500,0),
 (5,'CagePanel',(1,.08,2),3000,0),
 (6,'Portal',(4,.4,4),2500,0),
 (7,'CourtFence',(2,.12,3),3000,0),
 (8,'BasketHoop',(2,1.8,3.8),8000,0),
 (9,'Shelf',(1,.45,.08),1500,0),
 (10,'Fender',(1,.5,1.5),3500,0),
 (11,'ArrivalGantry',(4,.6,3),6000,0),
 (12,'DisplayPanel',(1,.12,2),3000,0),
 (13,'DisplayHook',(.12,.3,.18),1500,-1),
 (14,'Pillar',(.4,.4,4),2000,0),
 (15,'LegSection',(2.4,2.4,3),3000,0),
 (16,'UtilityCabinet',(1,.6,2),5000,1),
 (17,'PickupChute',(2,2,3),8000,0),
 (18,'Brace',(4,.3,.4),2000,0),
 (19,'Counter',(2,.8,1),4500,0),
 (20,'Pedestal',(1,1,.8),3500,0),
 (21,'Window',(4,.2,4),2500,0),
 (22,'RoofCassette',(4,4,.3),2000,0),
 (23,'WallPanel',(4,.2,4),2000,0),
]

def run():
    entries=json.loads((ROOT/'Library/HQBuild/archives.json').read_text(encoding='utf-8-sig'))
    records=[]
    for index,name,target,budget,turns in PARTS:
        dst=ROOT/'Assets/_Project/Models/HQ'/name
        dst.mkdir(parents=True,exist_ok=True)
        record_path=dst/'preparation.json'
        if record_path.exists():
            records.append(json.loads(record_path.read_text()));continue
        source=pathlib.Path(entries[index]['fbx'])
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(source))
        meshes=[o for o in bpy.data.objects if o.type=='MESH']
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:o.select_set(True)
        bpy.context.view_layer.objects.active=meshes[0]
        if len(meshes)>1:bpy.ops.object.join()
        low=bpy.context.active_object
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        low.rotation_euler.z=turns*math.pi/2
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        lo=Vector([min(v.co[i] for v in low.data.vertices) for i in range(3)])
        hi=Vector([max(v.co[i] for v in low.data.vertices) for i in range(3)])
        centre=(lo+hi)/2;size=hi-lo
        for v in low.data.vertices:
            v.co=Vector([(v.co[i]-(lo[i] if i==2 else centre[i]))*target[i]/size[i] for i in range(3)])
        if name == 'BasketHoop':
            # Meshy put the rim at 2.46 m and squashed it front-to-back.
            # Measured from the orange rim vertices; circular ~0.53 m outside,
            # 3.05 m high. Stretch the post, compress the oversized upper board.
            for v in low.data.vertices:
                v.co.x *= .836
                v.co.y *= 1.55
                z=v.co.z
                v.co.z=z*3.05/2.46 if z<=2.46 else 3.05+(z-2.46)*.9/1.34
        low.data.update()
        fitted_size=[max(v.co[i] for v in low.data.vertices)-min(v.co[i] for v in low.data.vertices) for i in range(3)]
        low.name=low.data.name=name
        # Explicit source maps: Meshy's embedded references can point at a different PC.
        mat=bpy.data.materials.new(name+'_Source');mat.use_nodes=True
        nodes=mat.node_tree.nodes; links=mat.node_tree.links; bsdf=nodes.get('Principled BSDF')
        for suffix,socket in [('_texture.png','Base Color'),('_texture_normal.png','Normal')]:
            tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(next(source.parent.glob('*'+suffix))))
            if socket=='Normal':
                tex.image.colorspace_settings.name='Non-Color';normal=nodes.new('ShaderNodeNormalMap')
                links.new(tex.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs[socket])
            else:links.new(tex.outputs['Color'],bsdf.inputs[socket])
        low.data.materials.clear();low.data.materials.append(mat)
        original=len(low.data.polygons)
        high=low.copy();high.data=low.data.copy();bpy.context.collection.objects.link(high)
        modifier=low.modifiers.new('Game budget','DECIMATE');modifier.ratio=min(1,budget/original);modifier.use_collapse_triangulate=True
        bpy.context.view_layer.objects.active=low;bpy.ops.object.modifier_apply(modifier=modifier.name)
        shade_smooth(low);unwrap_fresh(low)
        bake_from(high,low,name,2048 if max(target)>=3 else 1024,.15)
        maps=dst/'Maps';maps.mkdir(exist_ok=True)
        for role in ['BaseColor','Normal']:
            img=bpy.data.images[name+'_'+role]
            fmt=bpy.context.scene.render.image_settings;fmt.file_format='JPEG' if role=='BaseColor' else 'PNG'
            fmt.color_mode='RGB';fmt.quality=94
            p=maps/(name+'_'+role+('.jpg' if role=='BaseColor' else '.png'))
            img.save_render(str(p));img.filepath=str(p)
        bpy.ops.object.select_all(action='DESELECT');low.select_set(True)
        bpy.ops.export_scene.fbx(filepath=str(dst/(name+'.fbx')),use_selection=True,object_types={'MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='OFF',path_mode='STRIP',embed_textures=False)
        record={'part':name,'source_archive':entries[index]['zip'],'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'original_triangles':original,'triangles':sum(len(p.vertices)-2 for p in low.data.polygons),'unity_dimensions':[round(fitted_size[i],4) for i in (0,2,1)],'pivot':'bottom centre','maps':'rebaked base colour and tangent normal','source':'User-provided Meshy export, 25 September 2026'}
        record_path.write_text(json.dumps(record,indent=2)+'\n');records.append(record)
        print('HQ_PREPARED',name,flush=True)
    (ROOT/'Library/HQBuild/prepared.json').write_text(json.dumps(records,indent=2))
    print('HQ_PREPARATION_COMPLETE',len(records),flush=True)

run()
