// Exercise the embedded page's sign-in and withdrawal controls without external packages.
const fs=require('node:fs'), vm=require('node:vm'), assert=require('node:assert/strict');
const html=fs.readFileSync('Security/portal.html','utf8');
const elements=new Map();for(const id of html.matchAll(/id="([^"]+)"/g))elements.set(id[1],{value:'',hidden:false,disabled:false,textContent:''});
const get=id=>elements.get(id), calls=[];let fail=false;
get('transactions').hidden=true;
const context={URL,Error,location:{href:'https://example.invalid/economy/',pathname:'/economy/',hash:''},history:{replaceState(){}},document:{getElementById:get,querySelectorAll:()=>['login','savePin','quote','confirm'].map(get)},
 fetch:async(url,options)=>{const body=JSON.parse(options.body);calls.push({url:String(url),options,body});return {ok:!fail,json:async()=>fail?{error:'Session expired. Run /arkos security and enter a new access code.'}:body.action==='login'?{token:'A'.repeat(64)}:body.action==='status'?{wallet:'ARK-test',currency:'ARKOS',pinSet:true,message:'Ready'}:{message:'Success'}};}};
vm.createContext(context);vm.runInContext(html.split('<script nonce="NONCE_VALUE">')[1].split('</script>')[0],context);
(async()=>{
 get('account').value='Player';get('code').value='123456';
 await get('loginForm').onsubmit({preventDefault(){}});
 assert.equal(calls[0].body.code,'123456');assert.equal(calls[0].options.headers.Authorization,undefined);
 assert.equal(calls[1].options.headers.Authorization,'Bearer '+'A'.repeat(64));
 assert.equal(get('code').value,'');assert.equal(get('transactions').hidden,false);assert.equal(get('access').hidden,true);
 get('amount').value='0.01';get('pin').value='987654';await get('quote').onclick();assert.equal(get('confirm').disabled,false);
 await get('confirm').onclick();assert.equal(get('confirm').disabled,true);assert.equal(get('pin').value,'');
 fail=true;await get('quote').onclick();assert.equal(get('transactions').hidden,true);assert.equal(get('access').hidden,false);
 assert.ok(calls.every(c=>c.url==='https://example.invalid/economy/api'));
 console.log('PASS: 12 portal UI checks.');
})().catch(error=>{console.error(error);process.exitCode=1;});
