"""Run the real signer process against a loopback fake node. No live keys or broadcasts."""
import http.server, json, os, pathlib, subprocess, sys, threading, time, urllib.request, urllib.error
root = pathlib.Path(__file__).resolve().parents[1]
checks = 0
calls = []
class Node(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        from urllib.parse import parse_qs
        args = {k:v[0] for k,v in parse_qs(self.rfile.read(int(self.headers['Content-Length'])).decode()).items()}
        calls.append(args)
        if args['requestType'] == 'getAccountId': result = {'account':'100'}
        else:
            assert args['broadcast'] == 'false'
            assert args['feeNQT'] == '0'
            result = {'broadcasted':False,'transactionBytes':'aa'*100,'transactionJSON':{'sender':'100','recipient':args['recipient'],'feeNQT':'1000000'}}
        raw=json.dumps(result).encode();self.send_response(200);self.send_header('Content-Length',str(len(raw)));self.end_headers();self.wfile.write(raw)
    def log_message(self,*args): pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Node)
threading.Thread(target=server.serve_forever,daemon=True).start()
key='test-only-signer-key-32-characters-long'
def request(data, token=key):
    req=urllib.request.Request('http://127.0.0.1:4892/prepare',data=json.dumps(data).encode(),headers={'Content-Type':'application/json','X-Arkovia-Signer-Key':token})
    try:
        with urllib.request.urlopen(req,timeout=5) as r:return r.status,json.loads(r.read())
    except urllib.error.HTTPError as e:return e.code,e.read()
try:
    for currency in ['', '123']:
        env=dict(os.environ,ARKOVIA_SIGNER_API_KEY=key,ARKOVIA_RESERVE_SECRET='fake-test-secret-never-used-on-chain',ARKOVIA_RESERVE_ACCOUNT_ID='100',ARKOVIA_CURRENCY_ID=currency,ARKOVIA_SIGNER_MAX_UNITS='1000000000',ARKOVIA_SIGNER_NODE_URL=f'http://127.0.0.1:{server.server_port}/nxt')
        process=subprocess.Popen([sys.argv[1] if len(sys.argv)>1 else 'dotnet',str(root/'services/ArkoviaSigner/bin/Release/net9.0/ArkoviaSigner.dll')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        try:
            data={'currencyId':currency,'recipient':'300','units':'100','recipientPublicKey':'1'*64}
            for _ in range(100):
                if process.poll() is not None:raise RuntimeError('Signer exited before listening')
                try:status,_=request(data,'wrong');break
                except urllib.error.URLError:time.sleep(.05)
            else:raise RuntimeError('Signer did not start')
            assert status==401;checks+=1
            status,result=request(data);assert status==200 and result['transactionBytes']=='aa'*100;checks+=1
            assert calls[-1]['requestType']==('sendMoney' if currency=='' else 'transferCurrency');checks+=1
            assert request(dict(data,currencyId='999'))[0]==400;checks+=1
            assert request(dict(data,units='1000000001'))[0]==400;checks+=1
            assert request(dict(data,units='-1'))[0]==400;checks+=1
            assert request(dict(data,recipientPublicKey='bad'))[0]==400;checks+=1
        finally:
            process.terminate();process.wait(timeout=10)
finally:server.shutdown();server.server_close()
print(f'PASS: {checks} signer process checks (native and custom currencies).')
