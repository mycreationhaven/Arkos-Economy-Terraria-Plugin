(()=>{
  const categories=[
    ['all','All'],['weapons','Weapons'],['armor','Armor'],['potions','Potions'],['materials','Materials'],['building','Building'],['tools','Tools'],['other','Other']
  ];

  const categoryFor=name=>{
    const n=String(name||'').toLowerCase();
    if(/sword|bow|gun|staff|wand|spear|flail|yoyo|boomerang|whip|knife|blade|launcher|cannon|musket|pistol|rifle/.test(n))return'weapons';
    if(/helmet|mask|hood|breastplate|plate|greaves|armor|robe|hat|shirt|pants/.test(n))return'armor';
    if(/potion|flask|elixir|food|ale|soup|pie|fish dinner/.test(n))return'potions';
    if(/ore|bar|gel|lens|bone|silk|leather|feather|soul|crystal|fragment|scale|tissue|sample|wood|stone|sand|dirt|mud|clay/.test(n))return'materials';
    if(/wall|block|brick|platform|door|chair|table|work bench|workbench|torch|lantern|lamp|fence|beam|paint|wire/.test(n))return'building';
    if(/pickaxe|drill|axe|hammer|wrench|rod|bucket|mirror|hook/.test(n))return'tools';
    return'other';
  };

  function decorateGrid(grid){
    if(!grid)return;
    grid.querySelectorAll('.card').forEach(card=>{
      const title=card.querySelector('h3')?.textContent||'';
      card.dataset.category=categoryFor(title);
      if(!card.querySelector('.rarity-rune')){
        const rune=document.createElement('span');
        rune.className='rarity-rune';
        rune.setAttribute('aria-hidden','true');
        rune.textContent='◆';
        card.prepend(rune);
      }
    });
  }

  function makeToolbar(id,targetId,placeholder){
    const target=document.getElementById(targetId);
    if(!target||document.getElementById(id))return;
    const bar=document.createElement('div');
    bar.id=id;bar.className='market-toolbar game-panel compact-panel';
    bar.innerHTML=`<div class="category-tabs">${categories.map(([v,l])=>`<button type="button" class="category-tab${v==='all'?' active':''}" data-category="${v}">${l}</button>`).join('')}</div><label class="market-search"><span>Search</span><input type="search" placeholder="${placeholder}" autocomplete="off"></label>`;
    target.before(bar);
    let active='all';
    const apply=()=>{
      decorateGrid(target);
      const q=bar.querySelector('input').value.trim().toLowerCase();
      target.querySelectorAll('.card').forEach(card=>{
        const cat=card.dataset.category||'other';
        const text=card.textContent.toLowerCase();
        card.hidden=!((active==='all'||cat===active)&&(!q||text.includes(q)));
      });
    };
    bar.querySelectorAll('.category-tab').forEach(btn=>btn.addEventListener('click',()=>{
      active=btn.dataset.category;
      bar.querySelectorAll('.category-tab').forEach(x=>x.classList.toggle('active',x===btn));
      apply();
    }));
    bar.querySelector('input').addEventListener('input',apply);
    new MutationObserver(apply).observe(target,{childList:true,subtree:false});
    apply();
  }

  function ambientToggle(){
    const header=document.querySelector('.topbar');if(!header||document.getElementById('ambientToggle'))return;
    const b=document.createElement('button');b.id='ambientToggle';b.className='ambient-toggle secondary';b.type='button';b.title='Toggle day / night ambience';b.textContent='☀ Day';
    const saved=localStorage.getItem('arkovia-theme-time');
    if(saved==='night'){document.body.classList.add('night-mode');b.textContent='☾ Night';}
    b.addEventListener('click',()=>{
      const night=document.body.classList.toggle('night-mode');
      b.textContent=night?'☾ Night':'☀ Day';
      localStorage.setItem('arkovia-theme-time',night?'night':'day');
    });
    header.appendChild(b);
  }

  function addWorldStatus(){
    const hero=document.querySelector('.hero-side');if(!hero||hero.querySelector('.world-status'))return;
    const box=document.createElement('div');box.className='world-status';box.innerHTML='<span class="status-dot"></span><span>Arkovia Trading Post</span><strong>ONLINE</strong>';
    hero.appendChild(box);
  }

  function init(){
    makeToolbar('marketToolbar','listingGrid','Search marketplace…');
    makeToolbar('inventoryToolbar','inventoryGrid','Search backpack…');
    ambientToggle();addWorldStatus();
    ['listingGrid','inventoryGrid','assetGrid','myListingGrid','purchaseGrid','stockHoldingGrid'].forEach(id=>decorateGrid(document.getElementById(id)));
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);else init();
})();
