import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { clamp } from './scenario';
import type { FloodGeometry, FloodSample } from './results';

export interface SceneControls { reset(): void; zoom(direction: number): void; overhead(): void }
export interface FloodSceneProps {
  geometry: FloodGeometry | null;
  sample: FloodSample | null;
  minute: number;
  playing: boolean;
  active: boolean;
  showRain: boolean;
  showContours: boolean;
  showBuildings: boolean;
  reducedMotion: boolean;
  controls: React.RefObject<SceneControls | null>;
}

const riverX = (z: number) => Math.sin((z + 24) * 0.034) * 11 + Math.sin((z + 24) * 0.074) * 3;
const waterVertex = `
attribute float ground;
attribute vec2 velocity;
attribute float wetCell;
varying float cellIsWet;
varying vec3 worldPosition;
varying float depth;
varying vec2 flow;
void main() {
  cellIsWet = wetCell;
  worldPosition = position;
  depth = position.y - ground;
  flow = velocity;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0);
}`;
const waterFragment = `
uniform float clockTime;
varying float cellIsWet;
varying vec3 worldPosition;
varying float depth;
varying vec2 flow;
void main(){
  if(depth < .01 || cellIsWet < .5) discard;
  vec2 p = worldPosition.xz - flow * clockTime / 6.;
  float pattern = sin(p.x*1.3+p.y*.8) * sin(p.y*1.4+p.x*.2);
  vec3 color = mix(vec3(.24,.49,.63),vec3(.055,.23,.35),clamp(depth/12.,0.,1.));
  color += pattern*.025;
  color = mix(color,vec3(.67,.81,.86),(1.-smoothstep(.01,.35,depth))*.7);
  gl_FragColor = vec4(color,.94);
}`;

/** Display transform is 1:6 horizontally. Surface heights and vectors come from recorded SWE output. */
export function FloodScene(props: FloodSceneProps) {
  const host = useRef<HTMLDivElement>(null);
  const latest = useRef(props);
  latest.current = props;
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [restart, setRestart] = useState(0);

  useEffect(() => {
    const element = host.current;
    const model = props.geometry;
    if (!element || !model) return;
    const xs = [...new Set(model.vertices.map(v=>v[0]/6))].sort((a,b)=>a-b);
    const zs = [...new Set(model.vertices.map(v=>v[1]/6))].sort((a,b)=>a-b);
    const elevation = (x:number,z:number) => {
      const ix=clamp(xs.reduce((last,v,i)=>v<=x?i:last,0),0,xs.length-2), iz=clamp(zs.reduce((last,v,i)=>v<=z?i:last,0),0,zs.length-2);
      const tx=clamp((x-xs[ix])/(xs[ix+1]-xs[ix])),tz=clamp((z-zs[iz])/(zs[iz+1]-zs[iz]));
      const a=model.vertices[iz*xs.length+ix][2],b=model.vertices[iz*xs.length+ix+1][2];
      const c=model.vertices[(iz+1)*xs.length+ix][2],d=model.vertices[(iz+1)*xs.length+ix+1][2];
      return (a+(b-a)*tx)*(1-tz)+(c+(d-c)*tx)*tz;
    };
    setError(null); setReady(false);
    let renderer: THREE.WebGLRenderer;
    try { renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' }); }
    catch { setError('浏览器暂时无法创建三维视图。请开启硬件加速后重试，参数与时间轴仍可使用。'); return; }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.75));
    renderer.setClearColor('#242c33');
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.2;
    const textures:THREE.Texture[]=[];
    const manager=new THREE.LoadingManager(()=>setReady(true));
    manager.onError=()=>setError('场景材质加载失败，请重试。');
    const textureLoader=new THREE.TextureLoader(manager);
    const loadTexture=(file:string,color=false)=>{
      const texture=textureLoader.load(`${import.meta.env.BASE_URL}materials/flood/${file}`);
      texture.wrapS=texture.wrapT=THREE.RepeatWrapping;
      texture.anisotropy=Math.min(8,renderer.capabilities.getMaxAnisotropy());
      if(color)texture.colorSpace=THREE.SRGBColorSpace;
      textures.push(texture);return texture;
    };
    const groundColor=loadTexture('rocky_terrain_02_diff_1k.jpg',true);
    const groundNormal=loadTexture('rocky_terrain_02_nor_gl_1k.jpg');
    const groundArm=loadTexture('rocky_terrain_02_arm_1k.jpg');
    const concreteColor=loadTexture('concrete_floor_worn_001_diff_1k.jpg',true);
    const concreteNormal=loadTexture('concrete_floor_worn_001_nor_gl_1k.jpg');
    const concreteArm=loadTexture('concrete_floor_worn_001_arm_1k.jpg');
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    const canvas = renderer.domElement;
    canvas.setAttribute('aria-label', '河谷推演结果，可拖动旋转、滚轮缩放');
    canvas.setAttribute('role', 'img');
    canvas.tabIndex = 0;
    element.appendChild(canvas);

    const scene = new THREE.Scene();
    scene.fog = new THREE.FogExp2('#242c33', 0.0018);
    const camera = new THREE.PerspectiveCamera(37, 1, 0.2, 900);
    const defaultCamera = new THREE.Vector3(145, 205, 185);
    camera.position.copy(defaultCamera);
    const orbit = new OrbitControls(camera, canvas);
    orbit.target.set(0, 0, 4);
    orbit.enableDamping = true;
    orbit.dampingFactor = 0.075;
    orbit.minDistance = 80;
    orbit.maxDistance = 370;
    orbit.maxPolarAngle = Math.PI * 0.47;
    orbit.minPolarAngle = 0.1;
    orbit.enablePan = true;
    orbit.update();
    props.controls.current = {
      reset() { camera.position.copy(defaultCamera); orbit.target.set(0, 0, 4); orbit.update(); },
      zoom(direction) { camera.position.sub(orbit.target).multiplyScalar(direction > 0 ? 0.85 : 1.18).add(orbit.target); orbit.update(); },
      overhead() { camera.position.set(0, 250, 5); orbit.target.set(0, 0, 4); orbit.update(); },
    };
    const key = (e: KeyboardEvent) => {
      if (e.key === '+' || e.key === '=') { e.preventDefault(); props.controls.current?.zoom(1); }
      if (e.key === '-') { e.preventDefault(); props.controls.current?.zoom(-1); }
      if (e.key === 'Home') { e.preventDefault(); props.controls.current?.reset(); }
    };
    canvas.addEventListener('keydown', key);
    scene.add(new THREE.HemisphereLight('#e4e8e9', '#45413c', 2.3));
    const sun = new THREE.DirectionalLight('#fff1dc', 3.4);
    sun.position.set(-65, 130, 30);
    sun.castShadow = true;
    sun.shadow.mapSize.set(1024, 1024);
    Object.assign(sun.shadow.camera, { left: -110, right: 110, top: 130, bottom: -130, near: 1, far: 350 });
    sun.shadow.bias = -0.001;
    scene.add(sun);
    const fill = new THREE.DirectionalLight('#9cacbd', 0.75); fill.position.set(70, 40, -90); scene.add(fill);

    const terrainPositions:number[]=[],ground:number[]=[],cellForVertex:number[]=[],nodeForVertex:number[]=[];
    const low = new THREE.Color('#d4c9ad'), high = new THREE.Color('#efeee4');
    model.cells.forEach((corners,cell)=>{
      for(const corner of [0,1,2,0,2,3]){
        const [x,z,y]=model.vertices[corners[corner]];
        terrainPositions.push(x/6,y,z/6);ground.push(y);cellForVertex.push(cell);nodeForVertex.push(corners[corner]);
      }
    });
    const terrainGeo=new THREE.BufferGeometry();
    terrainGeo.setAttribute('position',new THREE.Float32BufferAttribute(model.vertices.flatMap(([x,z,y])=>[x/6,y,z/6]),3));
    terrainGeo.setAttribute('uv',new THREE.Float32BufferAttribute(model.vertices.flatMap(([x,z])=>[(x+420)/90,(z+600)/90]),2));
    terrainGeo.setAttribute('color',new THREE.Float32BufferAttribute(model.vertices.flatMap(([, ,y])=>{
      const c=low.clone().lerp(high,clamp((y-5)/45));return [c.r,c.g,c.b];
    }),3));
    terrainGeo.setIndex(model.cells.flatMap(c=>[c[0],c[2],c[1],c[0],c[3],c[2]]));
    terrainGeo.computeVertexNormals();
    const terrain = new THREE.Mesh(terrainGeo, new THREE.MeshStandardMaterial({vertexColors:true,map:groundColor,normalMap:groundNormal,
      normalScale:new THREE.Vector2(1.15,1.15),roughnessMap:groundArm,aoMap:groundArm,aoMapIntensity:.7,roughness:1,side:THREE.DoubleSide}));
    terrain.receiveShadow = true; scene.add(terrain);

    // A cut terrain edge gives the scene the legibility of a physical terrain model.
    const skirtPositions: number[] = [];
    const addEdge = (x1: number, z1: number, x2: number, z2: number) => {
      skirtPositions.push(x1,elevation(x1,z1),z1,x1,-7,z1,x2,elevation(x2,z2),z2, x2,elevation(x2,z2),z2,x1,-7,z1,x2,-7,z2);
    };
    for(let i=0;i<140;i++) { addEdge(i-70,-100,i-69,-100); addEdge(i-69,100,i-70,100); }
    for(let i=0;i<200;i++) { addEdge(-70,i-99,-70,i-100); addEdge(70,i-100,70,i-99); }
    const skirtGeo = new THREE.BufferGeometry(); skirtGeo.setAttribute('position',new THREE.Float32BufferAttribute(skirtPositions,3)); skirtGeo.computeVertexNormals();
    scene.add(new THREE.Mesh(skirtGeo,new THREE.MeshStandardMaterial({ color:'#474a45',roughness:1,side:THREE.DoubleSide })));
    const plinth = new THREE.Mesh(new THREE.BoxGeometry(141,2,201),new THREE.MeshStandardMaterial({color:'#303836',roughness:0.8})); plinth.position.y=-8; scene.add(plinth);

    // Marching-square intersections: actual equal-height contours of this procedural terrain.
    const contourPoints: number[] = [];
    for (let y=6;y<55;y+=3) for (let z=-100;z<100;z+=2) for (let x=-70;x<70;x+=2) {
      const corners = [[x,z],[x+2,z],[x+2,z+2],[x,z+2]], hits: number[][]=[];
      for(let edge=0;edge<4;edge++) {
        const a=corners[edge],b=corners[(edge+1)%4],ha=elevation(a[0],a[1]),hb=elevation(b[0],b[1]);
        if((ha<=y&&hb>y)||(hb<=y&&ha>y)){ const t=(y-ha)/(hb-ha);hits.push([a[0]+(b[0]-a[0])*t,y+0.09,a[1]+(b[1]-a[1])*t]); }
      }
      for(let j=0;j+1<hits.length;j+=2)contourPoints.push(...hits[j],...hits[j+1]);
    }
    const contourGeo=new THREE.BufferGeometry(); contourGeo.setAttribute('position',new THREE.Float32BufferAttribute(contourPoints,3));
    const contours=new THREE.LineSegments(contourGeo,new THREE.LineBasicMaterial({color:'#dfded3',transparent:true,opacity:0.17}));scene.add(contours);

    const waterGeo = new THREE.BufferGeometry();
    waterGeo.setAttribute('position',new THREE.Float32BufferAttribute(terrainPositions,3));
    waterGeo.setAttribute('ground',new THREE.Float32BufferAttribute(ground,1));
    waterGeo.setAttribute('wetCell',new THREE.Float32BufferAttribute(new Float32Array(ground.length),1));
    waterGeo.setAttribute('velocity',new THREE.Float32BufferAttribute(new Float32Array(ground.length*2),2));
    const waterMat = new THREE.ShaderMaterial({vertexShader:waterVertex,fragmentShader:waterFragment,transparent:true,depthWrite:false,side:THREE.DoubleSide,
      uniforms:{clockTime:{value:0}}});
    const water=new THREE.Mesh(waterGeo,waterMat);water.frustumCulled=false;water.renderOrder=2;scene.add(water);
    const dam=new THREE.Group();scene.add(dam);
    const damMaterial=new THREE.MeshStandardMaterial({color:'#e3ded0',map:concreteColor,normalMap:concreteNormal,
      normalScale:new THREE.Vector2(.65,.65),roughnessMap:concreteArm,aoMap:concreteArm,aoMapIntensity:.55,roughness:1});
    const damBlocks=model.damEdges.map(edge=>{
      const a=model.vertices[edge[0]],b=model.vertices[edge[1]];
      const bottom=Math.min(a[2],b[2]);
      const box=new THREE.BoxGeometry(Math.abs(a[0]-b[0])/6,1,3);
      const uv=box.attributes.uv;for(let i=0;i<uv.count;i++)uv.setXY(i,uv.getX(i)*Math.abs(a[0]-b[0])/12,uv.getY(i)*Math.max(1,model.damCrest-bottom)/5);
      const block=new THREE.Mesh(box,damMaterial);
      block.position.set((a[0]+b[0])/12,0,model.damY/6);block.castShadow=true;block.receiveShadow=true;dam.add(block);
      return {block,bottom};
    });

    // Reproducible vegetation and settlement positions; no external imagery or tile traffic.
    const rand=(n:number)=>{const v=Math.sin(n*127.1+311.7)*43758.5453;return v-Math.floor(v);};
    const trees=new THREE.InstancedMesh(new THREE.IcosahedronGeometry(1,1),new THREE.MeshStandardMaterial({color:'#536044',roughness:1}),1440);
    const trunks=new THREE.InstancedMesh(new THREE.CylinderGeometry(.09,.15,2.6,5),new THREE.MeshStandardMaterial({color:'#675441',roughness:1}),480);
    let count=0;const transform=new THREE.Object3D();
    for(let i=0;i<900&&count<480;i++) {
      const x=rand(i*2)*132-66,z=rand(i*2+1)*192-96,y=elevation(x,z);
      if(y<9||y>38||Math.abs(z+24)<5||Math.abs(x-riverX(z))<17)continue;
      const scale=.65+rand(i+80)*.8;
      transform.position.set(x,y+1.2*scale,z);transform.scale.setScalar(scale);transform.rotation.y=rand(i+51)*6.28;transform.updateMatrix();trunks.setMatrixAt(count,transform.matrix);
      for(let lobe=0;lobe<3;lobe++){
        const angle=lobe*2.1+rand(i)*6.28;transform.position.set(x+Math.cos(angle)*.45*scale,y+(2+lobe*.25)*scale,z+Math.sin(angle)*.45*scale);
        transform.scale.set(scale*(.75+rand(i+lobe)*.4),scale*(.9+rand(i+lobe+10)*.7),scale*.85);transform.updateMatrix();trees.setMatrixAt(count*3+lobe,transform.matrix);
        trees.setColorAt(count*3+lobe,new THREE.Color('#46583b').lerp(new THREE.Color('#7d8053'),rand(i+lobe+120)*.6));
      }
      count++;
    }
    trees.count=count*3;trunks.count=count;trees.castShadow=true;trunks.castShadow=true;scene.add(trees,trunks);
    const settlement=new THREE.Group();scene.add(settlement);
    const wallMaterial=new THREE.MeshStandardMaterial({color:'#e1dcd0',map:concreteColor,normalMap:concreteNormal,roughness:.9});
    const roofMaterial=new THREE.MeshStandardMaterial({color:'#7d7463',roughness:.85});
    for(let i=0;i<25;i++) {
      const z=33+Math.floor(i/5)*7,x=riverX(z)+16+(i%5)*4;
      const y=elevation(x,z),h=1.4+rand(i+90)*1.3;
      const house=new THREE.Mesh(new THREE.BoxGeometry(2.8,h,3.8),wallMaterial);house.position.set(x,y+h/2,z);house.castShadow=true;settlement.add(house);
      const roof=new THREE.Mesh(new THREE.ConeGeometry(2.7,1.1,4),roofMaterial);roof.rotation.y=Math.PI/4;roof.scale.z=1.2;roof.position.set(x,y+h+.5,z);settlement.add(roof);
    }
    const roadPoints:THREE.Vector3[]=[];
    for(let z=-80;z<=95;z+=2) {const x=riverX(z)+30;roadPoints.push(new THREE.Vector3(x,elevation(x,z)+.17,z));}
    const road=new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(roadPoints),130,.38,4,false),new THREE.MeshStandardMaterial({color:'#b8ac8e',roughness:1}));scene.add(road);

    const rainCount=1700,rainPositions=new Float32Array(rainCount*6);
    const rainGeo=new THREE.BufferGeometry();rainGeo.setAttribute('position',new THREE.BufferAttribute(rainPositions,3).setUsage(THREE.DynamicDrawUsage));
    const rain=new THREE.LineSegments(rainGeo,new THREE.LineBasicMaterial({color:'#a7b9c8',transparent:true,opacity:.27,depthWrite:false}));rain.frustumCulled=false;rain.renderOrder=4;scene.add(rain);
    const foamCount=model.cells.length,foamPositions=new Float32Array(foamCount*3);
    const foamGeo=new THREE.BufferGeometry();foamGeo.setAttribute('position',new THREE.BufferAttribute(foamPositions,3).setUsage(THREE.DynamicDrawUsage));
    const foam=new THREE.Points(foamGeo,new THREE.PointsMaterial({color:'#deecf0',size:.45,transparent:true,opacity:.72,depthWrite:false}));foam.renderOrder=3;foam.frustumCulled=false;scene.add(foam);

    const resize=()=>{const {width,height}=element.getBoundingClientRect();if(width<1||height<1)return;renderer.setSize(width,height);camera.aspect=width/height;camera.fov=camera.aspect<1.2?52:37;camera.updateProjectionMatrix();};
    const observer=new ResizeObserver(resize);observer.observe(element);resize();
    const nodeCount=model.vertices.length;
    const nodeSum=new Float32Array(nodeCount*2),nodeWeight=new Float32Array(nodeCount*2);
    let last=0,visualClock=0,disposed=false,visible=true;
    const animate=(time:number)=>{
      if(disposed)return;
      const p=latest.current;
      if(!p.active || !visible || document.hidden){last=time;return;}
      const dt=last?Math.min((time-last)/1000,.08):0;last=time;
      if(p.playing&&!p.reducedMotion)visualClock+=dt;
      const sample=p.sample, count=model.cells.length;
      waterMat.uniforms.clockTime.value=visualClock;
      const positions=waterGeo.attributes.position, velocities=waterGeo.attributes.velocity,wetCells=waterGeo.attributes.wetCell;
      nodeSum.fill(0);nodeWeight.fill(0);
      // Reconstruct a continuous display surface from wet cell values. Keep
      // separate nodes on either bank of the dam so its head is never averaged away.
      for(let cell=0;cell<count;cell++){
        const upstream=model.centers[cell][1]<model.damY;
        const level=sample?sample.values[cell]:(upstream?model.reservoirStage:-1000);
        const wet=sample?sample.values[count+cell]>.01:upstream;
        if(!wet)continue;
        for(const node of model.cells[cell]){const index=node+(upstream?0:nodeCount);nodeSum[index]+=level;nodeWeight[index]++;}
      }
      for(let i=0;i<positions.count;i++){
        const cell=cellForVertex[i],upstream=model.centers[cell][1]<model.damY,node=nodeForVertex[i]+(upstream?0:nodeCount);
        positions.setY(i,nodeWeight[node]>0?nodeSum[node]/nodeWeight[node]:-1000);
        wetCells.setX(i,(sample?sample.values[count+cell]>.01:upstream)?1:0);
        velocities.setXY(i,sample?.values[count*2+cell]??0,sample?.values[count*3+cell]??0);
      }
      positions.needsUpdate=true;velocities.needsUpdate=true;wetCells.needsUpdate=true;
      damBlocks.forEach(({block,bottom},i)=>{
        const top=sample?.damCrests[i]??Math.max(model.damCrest,bottom);
        block.scale.y=Math.max(.01,top-bottom);block.position.y=bottom+block.scale.y/2;
      });
      contours.visible=p.showContours;settlement.visible=p.showBuildings;
      const currentRain=sample?.rainRate??0;
      rain.visible=p.showRain&&currentRain>0&&!p.reducedMotion;
      rainGeo.setDrawRange(0,Math.floor(rainCount*currentRain/120)*2);
      if(rain.visible)for(let i=0;i<rainCount;i++){
        const x=rand(i*3)*144-72,z=rand(i*3+1)*204-102,y=16+((rand(i*3+2)*75-visualClock*35)%75+75)%75;
        rainPositions.set([x,y,z,x-.65,y+2.7,z-.35],i*6);
      }
      rainGeo.attributes.position.needsUpdate=true;
      foam.visible=Boolean(sample);
      if(sample)for(let i=0;i<foamCount;i++){
        const vx=sample.values[count*2+i],vy=sample.values[count*3+i],speed=Math.hypot(vx,vy);
        const phase=((visualClock*speed/10+rand(i))%1)-.5;
        const [x,z]=model.centers[i],level=sample.values[i];
        // Moving velocity glyphs stay inside their source cell; they are not generated flood fronts.
        const length=Math.min(8,Math.sqrt(model.areas[i])*.4),wet=sample.values[count+i]>.02&&speed>.03;
        foamPositions.set([x/6+(speed?vx/speed:0)*phase*length/6,wet?level+.08:-1000,z/6+(speed?vy/speed:0)*phase*length/6],i*3);
      }
      foamGeo.attributes.position.needsUpdate=true;
      orbit.update();renderer.render(scene,camera);
    };
    const syncLoop=()=>{last=0;renderer.setAnimationLoop(!document.hidden&&visible?animate:null);};
    const intersection=new IntersectionObserver(entries=>{visible=entries[0]?.isIntersecting??true;syncLoop();});intersection.observe(element);
    document.addEventListener('visibilitychange',syncLoop);
    const contextLost=(event:Event)=>{event.preventDefault();renderer.setAnimationLoop(null);setError('三维绘图连接已中断，场景参数仍保留。');};
    canvas.addEventListener('webglcontextlost',contextLost);
    syncLoop();
    return()=>{
      disposed=true;renderer.setAnimationLoop(null);observer.disconnect();intersection.disconnect();
      document.removeEventListener('visibilitychange',syncLoop);canvas.removeEventListener('keydown',key);canvas.removeEventListener('webglcontextlost',contextLost);
      orbit.dispose();props.controls.current=null;
      const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>();
      scene.traverse(node=>{const mesh=node as THREE.Mesh;if(mesh.geometry)geometries.add(mesh.geometry);if(mesh.material)(Array.isArray(mesh.material)?mesh.material:[mesh.material]).forEach(m=>materials.add(m));});
      geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());trees.dispose();trunks.dispose();textures.forEach(texture=>texture.dispose());renderer.dispose();renderer.forceContextLoss();canvas.remove();
    };
  },[restart,props.geometry]);

  return <div className="flood-scene" ref={host} data-scene-ready={ready && !error}>
    {!ready&&!error&&<div className="flood-scene-message"><span className="flood-loader"/><strong>正在构建三维河谷</strong><span>地形、水体与降雨</span></div>}
    {error&&<div className="flood-scene-message" role="alert"><strong>三维视图暂不可用</strong><p>{error}</p><button onClick={()=>setRestart(v=>v+1)}>重新加载三维场景</button></div>}
  </div>;
}
