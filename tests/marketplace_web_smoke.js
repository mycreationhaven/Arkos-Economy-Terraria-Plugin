const fs=require('fs');
function read(p){return fs.readFileSync(p,'utf8');}
function requireText(text,needle,label){if(!text.includes(needle))throw new Error(`Missing ${label}: ${needle}`);}
function rejectText(text,needle,label){if(text.includes(needle))throw new Error(`Unsafe ${label}: ${needle}`);}

const program=read('services/ArkoviaMarketplace/Program.cs');
const html=read('services/ArkoviaMarketplace/wwwroot/index.html');
const js=read('services/ArkoviaMarketplace/wwwroot/app.js');
const css=read('services/ArkoviaMarketplace/wwwroot/styles.css');

requireText(program,'HttpOnly = true','HttpOnly session cookie');
requireText(program,'SameSite = SameSiteMode.Strict','SameSite Strict cookie');
requireText(program,'Secure = cfg.CookieSecure','secure cookie control');
requireText(program,'X-CSRF-Token','CSRF requirement');
requireText(program,'Idempotency-Key','idempotency requirement');
requireText(program,'UseRateLimiter','rate limiting');
requireText(program,'Content-Security-Policy','CSP header');
requireText(program,'ARKOVIA_TSHOCK_REST_TOKEN','backend TShock token configuration');
requireText(program,'HMACSHA256','opaque stable subject derivation');
requireText(program,'session.WebSubject','server-side identity resolution');
requireText(program,'/marketplace/api/v1/mutate/list/','list mutation proxy');
requireText(program,'/marketplace/api/v1/mutate/buy/','buy mutation proxy');
requireText(program,'/marketplace/api/v1/mutate/cancel/','cancel mutation proxy');
requireText(html,'/market link','in-game linking instructions');
requireText(js,"crypto.randomUUID()",'browser idempotency generation');
requireText(js,"sessionStorage",'retry-stable idempotency key storage');
requireText(js,"'X-CSRF-Token'",'browser CSRF header');
requireText(css,'.grid','marketplace responsive layout');
rejectText(js,'ARKOVIA_TSHOCK_REST_TOKEN','TShock token absent from browser bundle');
rejectText(html,'ARKOVIA_TSHOCK_REST_TOKEN','TShock token absent from HTML');
rejectText(js,'tshockUserId','browser does not choose TShock user ID');

console.log('PASS: marketplace web security/UI smoke checks');
