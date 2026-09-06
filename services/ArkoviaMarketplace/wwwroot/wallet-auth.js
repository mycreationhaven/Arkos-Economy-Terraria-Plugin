(()=>{
  const KEY='arkovia-market-wallet';
  const $=id=>document.getElementById(id);
  const walletInput=()=>$('accountInput');
  const codeInput=()=>$('codeInput');

  function normalizeUi(){
    const panel=$('linkPanel');
    if(panel){
      const title=panel.querySelector('h2');
      if(title)title.textContent='Sign in with your ARKOS Wallet';
      const p=panel.querySelector('p');
      if(p)p.innerHTML='In Terraria, run <code>/market auth</code>. Enter your ARKOS wallet address and the 6-digit one-time authentication code shown in game. The code expires in 5 minutes and can only be used once.';
      const labels=panel.querySelectorAll('label');
      if(labels[0]){
        labels[0].childNodes[0].textContent='ARKOS wallet address';
        const input=walletInput();
        if(input){input.placeholder='ARKOS-…';input.autocomplete='username';}
      }
      if(labels[1])labels[1].childNodes[0].textContent='6-digit auth code';
      const submit=panel.querySelector('button[type="submit"]');
      if(submit)submit.textContent='Sign in';
    }
    const button=$('showLinkButton');
    if(button)button.textContent='Sign in with ARKOS Wallet';
    const chip=document.querySelector('.session-chip');
    if(chip&&chip.textContent.startsWith('Linked:'))chip.textContent=chip.textContent.replace('Linked:','Wallet:');
  }

  function prefill(){
    const input=walletInput();
    const saved=localStorage.getItem(KEY);
    if(input&&saved&&!input.value)input.value=saved;
  }

  async function walletSubmit(event){
    const form=$('linkForm');
    if(!form||event.target!==form)return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const wallet=(walletInput()?.value||'').trim();
    const code=(codeInput()?.value||'').trim();
    const msg=$('linkMessage');
    const button=form.querySelector('button[type="submit"]');
    if(!wallet||!/^[0-9]{6}$/.test(code)){
      if(msg){msg.textContent='Enter your ARKOS wallet address and 6-digit authentication code.';msg.className='message error';}
      return;
    }
    if(button)button.disabled=true;
    if(msg){msg.textContent='Authenticating wallet…';msg.className='message';}
    try{
      const response=await fetch('/api/auth/link',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json'},body:JSON.stringify({account:wallet,code})});
      const text=await response.text();
      let data={};try{data=text?JSON.parse(text):{};}catch{}
      if(!response.ok)throw new Error(data.error||data.Error||`Authentication failed (${response.status}).`);
      localStorage.setItem(KEY,wallet);
      if(codeInput())codeInput().value='';
      if(msg){msg.textContent='Wallet authenticated successfully.';msg.className='message success';}
      location.reload();
    }catch(error){
      if(msg){msg.textContent=error.message;msg.className='message error';}
    }finally{if(button)button.disabled=false;}
  }

  document.addEventListener('submit',walletSubmit,true);
  const observer=new MutationObserver(()=>{normalizeUi();prefill();});
  document.addEventListener('DOMContentLoaded',()=>{
    normalizeUi();prefill();
    observer.observe(document.body,{childList:true,subtree:true});
  });
})();
