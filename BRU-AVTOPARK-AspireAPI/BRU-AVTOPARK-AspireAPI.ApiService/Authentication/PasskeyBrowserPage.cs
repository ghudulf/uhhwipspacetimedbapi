namespace BRU_AVTOPARK_AspireAPI.ApiService.Authentication;

internal static class PasskeyBrowserPage
{
    public const string Html = """
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Ключ доступа — БРУ Автопарк</title><style>body{font:1.1rem system-ui;max-width:42rem;margin:3rem auto;padding:1rem}button{font:inherit;padding:.8rem}</style></head>
<body><main><h1>Ключ доступа</h1><p>Подтвердите действие в браузере. Windows Hello доступен, если его предлагает ваш браузер и устройство.</p>
<p id="status" role="status" aria-live="polite">Подготовка…</p><p id="retry" hidden>Повторите действие в приложении</p></main>
<script nonce="__NONCE__">
'use strict';
const url = new URL(location.href), f = url.searchParams.get('f'), b = url.searchParams.get('b');
history.replaceState(null, '', location.pathname);
const status = document.getElementById('status');
const decode = s => Uint8Array.from(atob(s.replace(/-/g,'+').replace(/_/g,'/') + '='.repeat((4-s.length%4)%4)), c=>c.charCodeAt(0));
const encode = value => btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g,'-').replace(/\//g,'_').replace(/=/g,'');
async function post(action, body) {
 const response = await fetch(new URL('./flows/'+encodeURIComponent(f)+'/'+action, location.href), {method:'POST',credentials:'omit',cache:'no-store',redirect:'error',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 if(!response.ok) throw new Error('ceremony_failed');
 return response.json();
}
(async()=>{
 try {
  if(!window.isSecureContext || !navigator.credentials || !window.PublicKeyCredential) { status.textContent='Браузер не поддерживает ключи доступа. Используйте современный браузер и защищённое соединение.'; return; }
  const data=await post('options',{browserToken:b}), key=data.publicKey;
  key.challenge=decode(key.challenge);
  for(const item of (key.allowCredentials||key.excludeCredentials||[])) item.id=decode(item.id);
  if(key.user) key.user.id=decode(key.user.id);
  status.textContent='Подтвердите действие с ключом доступа. Ожидание может занять до минуты.';
  const credential=data.purpose==='register'?await navigator.credentials.create({publicKey:key}):await navigator.credentials.get({publicKey:key});
  if(!credential) throw new Error('cancelled');
  const r=credential.response, response={clientDataJSON:encode(r.clientDataJSON)};
  if(data.purpose==='register') { response.attestationObject=encode(r.attestationObject); response.transports=r.getTransports?r.getTransports():[]; }
  else { response.authenticatorData=encode(r.authenticatorData); response.signature=encode(r.signature); response.userHandle=r.userHandle?encode(r.userHandle):null; }
  await post('assert',{browserToken:b,credential:{id:credential.id,rawId:encode(credential.rawId),type:credential.type,response,extensions:credential.getClientExtensionResults()}});
  status.textContent=data.purpose==='register'?'Ключ доступа зарегистрирован. Вернитесь в приложение.':'Вход выполнен. Вернитесь в приложение.';
 } catch(error) {
  status.textContent=error.name==='NotAllowedError'||error.name==='AbortError'?'Действие отменено или время ожидания истекло. Повторите попытку в приложении.':'Не удалось выполнить вход. Закройте окно и повторите попытку в приложении.';
  document.getElementById('retry').hidden=false;
 }
})();
</script></body></html>
""";
}
