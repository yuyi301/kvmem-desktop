// Text-only Ollama API compatibility adapter; inference remains in KVMem.
const http = require('node:http');
const fs = require('node:fs');
const {readSystemPrompt, withSystemPrompt} = require('./system-prompt.cjs');
const UPSTREAM = process.env.KVMEM_BRIDGE_UPSTREAM || 'http://127.0.0.1:18200';
const NAME = 'qwen-kvmem:256k';
const MODEL = process.env.KVMEM_BRIDGE_MODEL || 'RVN-IQ3_XXS-mtp.gguf';
const CTX = Number(process.env.KVMEM_BRIDGE_CONTEXT || 262144);
const MAX = Number(process.env.KVMEM_BRIDGE_MAX_OUTPUT || 20480);
const details = {format:'gguf',family:'qwen35',families:['qwen35'],parameter_size:'27B',quantization_level:'IQ3_XXS'};
const entry = {name:NAME,model:NAME,modified_at:'2026-09-19T00:00:00Z',size:0,digest:'kvmem-external-model',details};
const json=(res,status,data)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(data));};
const metadata=()=>({model:NAME,created_at:new Date().toISOString()});
async function readJson(req) {
  const chunks=[];let size=0;
  for await(const chunk of req){size+=chunk.length;if(size>16*1024*1024){const error=new Error('Request exceeds 16 MiB');error.status=413;throw error;}chunks.push(chunk);}
  return JSON.parse(Buffer.concat(chunks).toString('utf8')||'{}');
}
http.createServer(async(req,res)=>{
  try {
    const path=new URL(req.url,'http://localhost').pathname;
    if(req.method==='GET' && ['/', '/chat'].includes(path)) {res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});res.end(fs.readFileSync(require('node:path').join(__dirname,'chat.html')));return;}
    if(req.method==='GET' && path==='/desktop-config') return json(res,200,{systemPrompt:readSystemPrompt(),model:MODEL,maxTokens:MAX});
    if(path.startsWith('/v1/')) {
      let body;
      if(req.method==='POST') {
        const input=await readJson(req);
        if(path==='/v1/chat/completions') input.messages=withSystemPrompt(input.messages,readSystemPrompt());
        body=JSON.stringify(input);
      } else if(req.method!=='GET') return json(res,405,{error:'Method not supported'});
      const headers={...req.headers,host:new URL(UPSTREAM).host};delete headers['content-length'];delete headers['transfer-encoding'];delete headers.connection;
      if(body!==undefined)headers['content-length']=Buffer.byteLength(body);
      const target=new URL(path+new URL(req.url,'http://localhost').search,UPSTREAM);
      const proxy=http.request(target,{method:req.method,headers},incoming=>{res.writeHead(incoming.statusCode,incoming.headers);incoming.pipe(res);incoming.on('error',()=>res.destroy());});
      proxy.on('error',e=>{if(!res.headersSent)json(res,502,{error:'KVMem unavailable: '+e.message});else res.destroy();});
      res.on('close',()=>proxy.destroy());proxy.end(body);return;
    }
    if(req.method==='GET' && path==='/api/version') return json(res,200,{version:'0.0.0-kvmem-bridge'});
    if(req.method==='GET' && path==='/api/tags') return json(res,200,{models:[entry]});
    if(req.method==='GET' && path==='/api/ps') return json(res,200,{models:[{...entry,context_length:CTX}]});
    if(req.method!=='POST') return json(res,404,{error:'Unsupported bridge endpoint'});
    const input=await readJson(req);
    if(input.model && ![NAME,'qwen-kvmem',MODEL].includes(input.model)) return json(res,404,{error:'Use model '+NAME});
    if(path==='/api/show') return json(res,200,{details,capabilities:['completion','thinking'],parameters:'num_ctx '+CTX,model_info:{'general.architecture':'qwen35','qwen35.context_length':CTX},template:'{{ .Prompt }}'});
    if(!['/api/chat','/api/generate'].includes(path)) return json(res,404,{error:'Only text chat/generate are supported; model management stays in KVMem.'});
    if(input.raw || input.context?.length || input.images?.length || input.tools?.length || input.format || input.messages?.some(m=>m.images?.length||m.tool_calls?.length||m.role==='tool')) return json(res,400,{error:'This adapter supports text messages only; raw/context/images/tools/format are unsupported.'});
    const opts=input.options||{};
    if(opts.num_ctx>CTX) return json(res,400,{error:'Maximum context is '+CTX+' tokens'});
    if(opts.num_predict>MAX) return json(res,400,{error:'Maximum output including thinking is '+MAX+' tokens'});
    let messages=input.messages;
    if(path==='/api/generate') messages=[...(input.system?[{role:'system',content:input.system}]:[]),{role:'user',content:input.prompt||''}];
    if(!Array.isArray(messages)) return json(res,400,{error:'messages must be an array'});
    messages=withSystemPrompt(messages,readSystemPrompt());
    const stream=input.stream!==false;
    const body={model:MODEL,messages,stream,max_tokens:opts.num_predict>0?opts.num_predict:MAX};
    if(input.think===false) body.reasoning_effort='none';
    else if(typeof input.think==='string') body.reasoning_effort=input.think;
    for(const [a,b] of Object.entries({temperature:'temperature',top_p:'top_p',top_k:'top_k',min_p:'min_p',repeat_penalty:'repetition_penalty',presence_penalty:'presence_penalty',frequency_penalty:'frequency_penalty',seed:'seed',stop:'stop'})) if(opts[a]!==undefined) body[b]=opts[a];
    const start=Date.now();let first=null,usage={},timings={},finish='stop';
    const convert=(content='',thinking='',done=false)=>({...metadata(),...(path==='/api/chat'?{message:{role:'assistant',content,...(thinking?{thinking}:{})}}:{response:content,...(thinking?{thinking}:{})}),done,...(done?{done_reason:finish,total_duration:(Date.now()-start)*1e6,prompt_eval_count:usage.prompt_tokens||0,eval_count:usage.completion_tokens||timings.predicted_n||0,eval_duration:timings.predicted_ms?Math.round(timings.predicted_ms*1e6):(first?Math.round((Date.now()-first)*1e6):0)}:{})});
    const upstream=http.request(new URL('/v1/chat/completions',UPSTREAM),{method:'POST',headers:{'Content-Type':'application/json'}},incoming=>{
      incoming.setEncoding('utf8');let buffer='';
      if(incoming.statusCode!==200){incoming.on('data',c=>buffer+=c);incoming.on('end',()=>json(res,incoming.statusCode,{error:buffer}));return;}
      if(!stream){incoming.on('data',c=>buffer+=c);incoming.on('end',()=>{try{const d=JSON.parse(buffer);usage=d.usage||{};timings=d.timings||{};finish=d.choices?.[0]?.finish_reason||'stop';const m=d.choices?.[0]?.message||{};json(res,200,convert(m.content,m.reasoning_content,true));}catch(e){json(res,502,{error:e.message});}});return;}
      res.writeHead(200,{'Content-Type':'application/x-ndjson'});
      incoming.on('data',chunk=>{buffer+=chunk;let pos;while((pos=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,pos).trim();buffer=buffer.slice(pos+1);if(!line.startsWith('data:'))continue;const value=line.slice(5).trim();if(value==='[DONE]'){res.end(JSON.stringify(convert('','',true))+'\n');continue;}try{const d=JSON.parse(value);if(d.error){res.end(JSON.stringify({error:d.error})+'\n');return;}usage=d.usage||usage;timings=d.timings||timings;const c=d.choices?.[0];finish=c?.finish_reason||finish;const delta=c?.delta;if(delta?.content||delta?.reasoning_content){first??=Date.now();res.write(JSON.stringify(convert(delta.content,delta.reasoning_content))+'\n');}}catch(e){res.end(JSON.stringify({error:e.message})+'\n');}}});
      incoming.on('end',()=>{if(!res.writableEnded)res.end(JSON.stringify({error:'Upstream stream ended unexpectedly'})+'\n');});
      incoming.on('error',e=>{if(!res.writableEnded)res.end(JSON.stringify({error:e.message})+'\n');});
    });
    res.on('close',()=>upstream.destroy());
    upstream.on('error',e=>{if(!res.headersSent)json(res,502,{error:'KVMem unavailable: '+e.message});else if(!res.writableEnded)res.end(JSON.stringify({error:e.message})+'\n');});
    upstream.end(JSON.stringify(body));
  }catch(e){if(!res.headersSent)json(res,e.status||400,{error:e.message});else res.end();}
}).listen(Number(process.env.KVMEM_BRIDGE_PORT || 18201),process.env.KVMEM_BRIDGE_HOST || '127.0.0.1',()=>console.log('KVMem Ollama compatibility bridge listening on port 18201; model '+NAME));

