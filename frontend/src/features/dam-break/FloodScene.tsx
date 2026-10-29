import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { clamp, ease, rainAt, type FloodScenario } from './scenario';

export interface SceneControls { reset(): void; zoom(direction: number): void; overhead(): void }
export interface FloodSceneProps {
  scenario: FloodScenario;
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
const bed = (z: number) => 3.8 - (z + 24) * 0.035;
function elevation(x: number, z: number): number {
  const distance = Math.abs(x - riverX(z));
  const width = z < -25 ? 11 + Math.sin(clamp((-z - 25) / 70) * Math.PI) * 10 : 5.5 + clamp((z + 25) / 80) * 4;
  const flank = Math.max(0, distance - width);
  const ridge = Math.sin(z * 0.07 + x * 0.035) * 3.5 + Math.sin(x * 0.15 - z * 0.09) * 1.7 + Math.cos(z * 0.22 + x * 0.13) * 0.7;
  return bed(z) + flank * 0.32 + flank * flank * 0.0025 + ridge * ease(flank / 24) - Math.exp(-distance * distance / 28) * 1.5;
}

const waterVertex = `
uniform float clockTime;
uniform float opening;
uniform float front;
uniform float reservoir;
uniform float rainfall;
uniform float breachWidth;
attribute float ground;
varying vec3 worldPosition;
varying float depth;
varying float along;
void main() {
  vec3 p = position;
  float z = p.z;
  float floorHeight = 3.8 - (z + 24.0) * .035;
  float downstream = smoothstep(-26.0, -21.0, z);
  float waveFront = 1.0 - smoothstep(front - 7.0, front + 3.0, z);
  float lake = 8.0 + reservoir * .24 + rainfall * 1.4 - opening * 3.2;
  float flood = floorHeight + .25 + waveFront * opening * (4.6 + breachWidth * 2.3) * exp(-max(0., z+24.) * .006);
  float level = mix(lake, flood, downstream);
  p.y = level + sin(p.x*.58 + clockTime*1.5 + p.z*.32)*.10 + sin(p.z*.85-clockTime*2.4)*.07;
  depth = level - ground;
  worldPosition = p;
  along = downstream;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(p,1.0);
}`;
const waterFragment = `
uniform float clockTime;
uniform float opening;
varying vec3 worldPosition;
varying float depth;
varying float along;
void main(){
  if(depth < .08) discard;
  vec3 p = worldPosition;
  float pattern = sin(p.x*.91+p.z*.66-clockTime*2.) * sin(p.z*1.18+clockTime*1.4+p.x*.17);
  float ribbon = pow(max(0.,sin(p.z*2.3-clockTime*4.+sin(p.x*.7))), 18.0);
  vec3 deep = vec3(.055,.23,.35);
  vec3 shallow = vec3(.24,.49,.63);
  vec3 color = mix(shallow, deep, clamp(depth/12.,0.,1.));
  color += pattern*.022 + ribbon*.075*(.3+along*opening);
  float edge = 1.-smoothstep(.1,.55,depth);
  color = mix(color,vec3(.67,.81,.86),edge*.7);
  vec3 eye = normalize(cameraPosition-p);
  float fresnel = pow(1.-max(0.,eye.y),3.);
  color = mix(color,vec3(.61,.72,.81),fresnel*.25);
  gl_FragColor = vec4(color,.92);
}`;

/** Three.js concept landscape, deliberately independent of the numerical solver. */
export function FloodScene(props: FloodSceneProps) {
  const host = useRef<HTMLDivElement>(null);
  const latest = useRef(props);
  latest.current = props;
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [restart, setRestart] = useState(0);

  useEffect(() => {
    const element = host.current;
    if (!element) return;
    setError(null); setReady(false);
    let renderer: THREE.WebGLRenderer;
    try { renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' }); }
    catch { setError('浏览器暂时无法创建三维视图。请开启硬件加速后重试，参数与时间轴仍可使用。'); return; }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.75));
    renderer.setClearColor('#242c33');
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.2;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    const canvas = renderer.domElement;
    canvas.setAttribute('aria-label', '河谷三维演示，可拖动旋转、滚轮缩放');
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

    const terrainGeo = new THREE.PlaneGeometry(140, 200, 140, 200);
    terrainGeo.rotateX(-Math.PI / 2);
    const vertices = terrainGeo.attributes.position;
    const colors: number[] = [], ground: number[] = [];
    const low = new THREE.Color('#74756c'), high = new THREE.Color('#b0aa9c'), rock = new THREE.Color('#989a91');
    for (let i = 0; i < vertices.count; i++) {
      const x = vertices.getX(i), z = vertices.getZ(i), y = elevation(x, z);
      vertices.setY(i, y); ground.push(y);
      const color = low.clone().lerp(high, clamp((y - 5) / 45));
      color.lerp(rock, clamp(Math.sin(x * 0.28 + z * 0.19) * 0.18));
      colors.push(color.r, color.g, color.b);
    }
    terrainGeo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
    terrainGeo.computeVertexNormals();
    const terrain = new THREE.Mesh(terrainGeo, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.93, metalness: 0.02 }));
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

    const waterGeo = terrainGeo.clone();
    waterGeo.setAttribute('ground',new THREE.Float32BufferAttribute(ground,1));
    const waterMat = new THREE.ShaderMaterial({ vertexShader:waterVertex,fragmentShader:waterFragment,transparent:true,depthWrite:false,
      uniforms:{clockTime:{value:0},opening:{value:0},front:{value:-30},reservoir:{value:18},rainfall:{value:0},breachWidth:{value:0.6}} });
    const water=new THREE.Mesh(waterGeo,waterMat); water.frustumCulled=false; water.renderOrder=2;scene.add(water);

    const dam=new THREE.Group();scene.add(dam);
    const damMaterial=new THREE.MeshStandardMaterial({color:'#c2c3b4',roughness:.8});
    const damBlocks:THREE.Mesh[]=[];
    const damGeo=new THREE.BoxGeometry(2.1,12,4.5);
    for(let i=0;i<25;i++) {
      const block=new THREE.Mesh(damGeo,damMaterial);block.position.set((i-12)*2.1,10.4,-24);block.castShadow=true;block.receiveShadow=true;dam.add(block);damBlocks.push(block);
      const cap=new THREE.Mesh(new THREE.BoxGeometry(2.08,.5,5),new THREE.MeshStandardMaterial({color:'#e1ddc9',roughness:.8}));cap.position.y=6.2;block.add(cap);
    }
    const rail=new THREE.Mesh(new THREE.BoxGeometry(57,.25,.25),new THREE.MeshStandardMaterial({color:'#edd8a3'}));rail.position.set(0,17,-26.2);dam.add(rail);

    // Reproducible vegetation and settlement positions; no external imagery or tile traffic.
    const rand=(n:number)=>{const v=Math.sin(n*127.1+311.7)*43758.5453;return v-Math.floor(v);};
    const trees=new THREE.InstancedMesh(new THREE.ConeGeometry(.85,3.1,5),new THREE.MeshStandardMaterial({color:'#404b46',roughness:1}),480);
    let count=0;const transform=new THREE.Object3D();
    for(let i=0;i<900&&count<480;i++) {
      const x=rand(i*2)*132-66,z=rand(i*2+1)*192-96,y=elevation(x,z);
      if(y<9||y>38||Math.abs(z+24)<5||Math.abs(x-riverX(z))<17)continue;
      transform.position.set(x,y+1.4,z);transform.scale.setScalar(.65+rand(i+80)*.8);transform.rotation.y=rand(i+51)*6.28;transform.updateMatrix();trees.setMatrixAt(count++,transform.matrix);
    }
    trees.count=count;trees.castShadow=true;scene.add(trees);
    const settlement=new THREE.Group();scene.add(settlement);
    const wallMaterial=new THREE.MeshStandardMaterial({color:'#e1dcd0',roughness:.9});
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
    const foamCount=200,foamPositions=new Float32Array(foamCount*3);
    const foamGeo=new THREE.BufferGeometry();foamGeo.setAttribute('position',new THREE.BufferAttribute(foamPositions,3).setUsage(THREE.DynamicDrawUsage));
    const foam=new THREE.Points(foamGeo,new THREE.PointsMaterial({color:'#deecf0',size:.45,transparent:true,opacity:.72,depthWrite:false}));foam.renderOrder=3;foam.frustumCulled=false;scene.add(foam);

    const resize=()=>{const {width,height}=element.getBoundingClientRect();if(width<1||height<1)return;renderer.setSize(width,height);camera.aspect=width/height;camera.fov=camera.aspect<1.2?52:37;camera.updateProjectionMatrix();};
    const observer=new ResizeObserver(resize);observer.observe(element);resize();
    let last=0,visualClock=0,disposed=false,visible=true;
    const animate=(time:number)=>{
      if(disposed)return;
      const p=latest.current;
      if(!p.active || !visible || document.hidden){last=time;return;}
      const dt=last?Math.min((time-last)/1000,.08):0;last=time;
      if(p.playing&&!p.reducedMotion)visualClock+=dt;
      const s=p.scenario,opening=ease((p.minute-s.breachStart)/s.breachDuration);
      const wave=clamp((p.minute-s.breachStart)/65);
      const currentRain=rainAt(s,p.minute),front=-28+wave*150;
      waterMat.uniforms.clockTime.value=visualClock;
      waterMat.uniforms.opening.value=opening;
      waterMat.uniforms.front.value=front;
      waterMat.uniforms.reservoir.value=s.reservoirLevel;
      waterMat.uniforms.rainfall.value=clamp(p.minute/s.stormDuration)*s.rainfall/120;
      waterMat.uniforms.breachWidth.value=s.breachWidth/100;
      for(let i=0;i<damBlocks.length;i++){
        const x=Math.abs(i-12)*2.1,halfWidth=(s.breachWidth/100)*19;
        const lowered=opening*(1-ease((x-halfWidth*.65)/(halfWidth*.4)));
        damBlocks[i].scale.y=1-lowered*.93;damBlocks[i].position.y=4.4+6*damBlocks[i].scale.y;
      }
      rail.visible=opening<.12;
      contours.visible=p.showContours;settlement.visible=p.showBuildings;
      rain.visible=p.showRain&&currentRain>0&&!p.reducedMotion;
      const drops=Math.floor(rainCount*currentRain/120);rainGeo.setDrawRange(0,drops*2);
      if(rain.visible)for(let i=0;i<drops;i++) {
        const x=rand(i*3)*140-70,z=rand(i*3+1)*200-100;
        const y=5+((rand(i*3+2)*65-visualClock*32)%65+65)%65;
        rainPositions.set([x,y,z,x-.6,y+2.1,z+.25],i*6);
      }
      rainGeo.attributes.position.needsUpdate=true;
      foam.visible=opening>.05;
      if(foam.visible)for(let i=0;i<foamCount;i++){
        const progress=((rand(i+7)+visualClock*.08)%1),z=-24+progress*Math.max(1,front+24);
        const x=riverX(z)+(rand(i+800)-.5)*(7+opening*10);
        const level=bed(z)+.25+opening*(4.6+s.breachWidth*.023)*Math.exp(-Math.max(0,z+24)*.006);
        foamPositions.set([x,level+.22+Math.sin(i+visualClock*3)*.06,z],i*3);
      }
      foamGeo.attributes.position.needsUpdate=true;
      orbit.update();renderer.render(scene,camera);
    };
    const syncLoop=()=>{last=0;renderer.setAnimationLoop(!document.hidden&&visible?animate:null);};
    const intersection=new IntersectionObserver(entries=>{visible=entries[0]?.isIntersecting??true;syncLoop();});intersection.observe(element);
    document.addEventListener('visibilitychange',syncLoop);
    const contextLost=(event:Event)=>{event.preventDefault();renderer.setAnimationLoop(null);setError('三维绘图连接已中断，场景参数仍保留。');};
    canvas.addEventListener('webglcontextlost',contextLost);
    syncLoop();setReady(true);
    return()=>{
      disposed=true;renderer.setAnimationLoop(null);observer.disconnect();intersection.disconnect();
      document.removeEventListener('visibilitychange',syncLoop);canvas.removeEventListener('keydown',key);canvas.removeEventListener('webglcontextlost',contextLost);
      orbit.dispose();props.controls.current=null;
      const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>();
      scene.traverse(node=>{const mesh=node as THREE.Mesh;if(mesh.geometry)geometries.add(mesh.geometry);if(mesh.material)(Array.isArray(mesh.material)?mesh.material:[mesh.material]).forEach(m=>materials.add(m));});
      geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());trees.dispose();renderer.dispose();renderer.forceContextLoss();canvas.remove();
    };
  },[restart]);

  return <div className="flood-scene" ref={host} data-scene-ready={ready && !error}>
    {!ready&&!error&&<div className="flood-scene-message"><span className="flood-loader"/><strong>正在构建三维河谷</strong><span>地形、水体与降雨</span></div>}
    {error&&<div className="flood-scene-message" role="alert"><strong>三维视图暂不可用</strong><p>{error}</p><button onClick={()=>setRestart(v=>v+1)}>重新加载三维场景</button></div>}
  </div>;
}
