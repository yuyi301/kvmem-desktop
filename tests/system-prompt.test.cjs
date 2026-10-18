const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const http=require('node:http');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {spawn}=require('node:child_process');
const {once}=require('node:events');
const {withSystemPrompt,readSystemPrompt}=require('../system-prompt.cjs');
let root,upstream,child,base,received=[];
const prompt='你是一名助手。\n请使用中文回答，保留 "引号" 和 🐧。';
const save=text=>fs.writeFileSync(path.join(root,'system-prompt.json'),JSON.stringify({Text:text}));
async function post(route,data){const r=await fetch(base+route,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});return {status:r.status,text:await r.text()};}
before(async()=>{
 root=fs.mkdtempSync(path.join(os.tmpdir(),'kvmem-prompt-'));
 for(const f of ['ollama-kvmem-bridge.cjs','system-prompt.cjs','chat.html'])fs.copyFileSync(path.join(__dirname,'..',f),path.join(root,f));
 upstream=http.createServer(async(req,res)=>{
  if(req.method==='GET'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({data:[{id:'test.gguf'}]}));return;}
  const chunks=[];for await(const c of req)chunks.push(c);const body=JSON.parse(Buffer.concat(chunks));received.push(body);
  if(body.messages?.some(m=>m.content==='error')){res.writeHead(422);res.end('mock upstream error');return;}
  res.setHeader('Content-Type',body.stream?'text/event-stream':'application/json');
  if(body.stream){res.end('data: '+JSON.stringify({choices:[{delta:{content:'中文回答'},finish_reason:null}]})+'\n\ndata: '+JSON.stringify({choices:[{delta:{},finish_reason:'stop'}]})+'\n\ndata: [DONE]\n\n');}
  else res.end(JSON.stringify({choices:[{message:{role:'assistant',content:'收到'},finish_reason:'stop'}]}));
 });
 upstream.listen(0,'127.0.0.1');await once(upstream,'listening');
 const probe=http.createServer();probe.listen(0,'127.0.0.1');await once(probe,'listening');const port=probe.address().port;await new Promise(r=>probe.close(r));base='http://127.0.0.1:'+port;
 child=spawn(process.execPath,[path.join(root,'ollama-kvmem-bridge.cjs')],{windowsHide:true,env:{...process.env,KVMEM_BRIDGE_PORT:String(port),KVMEM_BRIDGE_UPSTREAM:'http://127.0.0.1:'+upstream.address().port,KVMEM_BRIDGE_MODEL:'test.gguf'}});
 await Promise.race([once(child.stdout,'data'),once(child,'exit').then(([code])=>{throw Error('bridge exited: '+code);}),new Promise((_,reject)=>{const t=setTimeout(()=>reject(Error('bridge start timed out')),10000);t.unref();})]);
});
after(async()=>{if(child){const exited=once(child,'exit');child.kill();await exited;}if(upstream)await new Promise(r=>upstream.close(r));if(root)fs.rmSync(root,{recursive:true,force:true});});
test('missing file and blank prompt preserve original messages',()=>{const messages=[{role:'user',content:'你好'}];assert.equal(readSystemPrompt(root),'');assert.equal(withSystemPrompt(messages,''),messages);assert.equal(withSystemPrompt(messages,' \r\n'),messages);});
test('OpenAI injects multiline Unicode default and retains image/tool parameters',async()=>{
 save(prompt);const data={model:'test.gguf',stream:false,messages:[{role:'user',content:[{type:'text',text:'图片中文'},{type:'image_url',image_url:{url:'data:image/png;base64,AAAA'}}]}],tools:[{type:'function',function:{name:'lookup'}}],temperature:.3};
 assert.equal((await post('/v1/chat/completions',data)).status,200);assert.deepEqual(received.at(-1),{...data,messages:[{role:'system',content:prompt},...data.messages]});
});
test('explicit system or developer including empty system wins without duplicates',async()=>{
 for(const role of ['system','developer'])for(const content of ['来自客户端','']){const messages=[{role,content},{role:'user',content:'hi'}];await post('/v1/chat/completions',{messages});assert.deepEqual(received.at(-1).messages,messages);}
});
test('saving or clearing applies without restarting bridge',async()=>{
 save('新的提示词');await post('/v1/chat/completions',{messages:[{role:'user',content:'hi'}]});assert.equal(received.at(-1).messages[0].content,'新的提示词');
 save('');await post('/v1/chat/completions',{messages:[{role:'user',content:'hi'}]});assert.equal(received.at(-1).messages.length,1);save(prompt);
});
test('Ollama chat and generate inject default, and explicit system overrides',async()=>{
 for(const route of ['/api/chat','/api/generate']){const data={stream:false,...(route.endsWith('chat')?{messages:[{role:'user',content:'hi'}]}:{prompt:'hi'})};assert.equal((await post(route,data)).status,200);assert.equal(received.at(-1).messages[0].content,prompt);}
 await post('/api/generate',{prompt:'hi',system:'客户端规则',stream:false});assert.deepEqual(received.at(-1).messages[0],{role:'system',content:'客户端规则'});
});
test('streaming OpenAI and Ollama responses retain Chinese output',async()=>{
 const data={messages:[{role:'user',content:'hi'}],stream:true};const native=await post('/v1/chat/completions',data);assert.match(native.text,/中文回答/);assert.match(native.text,/\[DONE\]/);
 const ollama=await post('/api/chat',data);const lines=ollama.text.trim().split('\n').map(JSON.parse);assert.equal(lines[0].message.content,'中文回答');assert.equal(lines.at(-1).done,true);
});
test('Unicode survives a request split inside a UTF-8 character',async()=>{
 const bytes=Buffer.from(JSON.stringify({messages:[{role:'user',content:'你好吗'}]}));const split=bytes.indexOf(Buffer.from('你'))+1;
 await new Promise((resolve,reject)=>{const req=http.request(base+'/v1/chat/completions',{method:'POST',headers:{'Content-Type':'application/json'}},r=>{r.resume();r.on('end',resolve);});req.on('error',reject);req.write(bytes.subarray(0,split));setTimeout(()=>req.end(bytes.subarray(split)),10);});
 assert.equal(received.at(-1).messages[1].content,'你好吗');
});
test('chat assets, model discovery, config, invalid input and upstream errors',async()=>{
 assert.match(await (await fetch(base+'/chat')).text(),/系统提示词/);assert.equal((await (await fetch(base+'/desktop-config')).json()).systemPrompt,prompt);assert.equal((await (await fetch(base+'/v1/models')).json()).data[0].id,'test.gguf');
 assert.equal((await post('/v1/chat/completions',{messages:'bad'})).status,400);assert.equal((await post('/v1/chat/completions',{messages:[{role:'user',content:'error'}]})).status,422);
});
test('absolute-form request targets stay pinned to the configured upstream',async()=>{
 await new Promise((resolve,reject)=>{const req=http.request(base,{method:'POST',path:'http://example.invalid/v1/chat/completions',headers:{'Content-Type':'application/json'}},r=>{assert.equal(r.statusCode,200);r.resume();r.on('end',resolve);});req.on('error',reject);req.end(JSON.stringify({messages:[{role:'user',content:'fixed upstream'}]}));});
 assert.equal(received.at(-1).messages.at(-1).content,'fixed upstream');
});
test('malformed prompt file fails visibly and BOM is accepted',async()=>{
 fs.writeFileSync(path.join(root,'system-prompt.json'),'broken');const count=received.length;const r=await post('/v1/chat/completions',{messages:[]});assert.equal(r.status,400);assert.match(r.text,/system-prompt.json/);assert.equal(received.length,count);
 fs.writeFileSync(path.join(root,'system-prompt.json'),'\uFEFF'+JSON.stringify({Text:prompt}));assert.equal(readSystemPrompt(root),prompt);
});
