import json, sys, time
r=json.loads(sys.stdin.readline()); m=r['method']
if m=='map.background.styles':
 for line in sys.stdin:
  x=json.loads(line)
  if x['control'].get('type')=='cancel':
   print(json.dumps({'id':r['id'],'result':{'state':'canceled'}}),flush=True); break
elif m=='slow':
 time.sleep(0.7);print(json.dumps({'id':r['id'],'result':{'value':'slow'}}),flush=True)
else:
 print(json.dumps({'id':r['id'],'result':{'value':'fast'}}),flush=True)
