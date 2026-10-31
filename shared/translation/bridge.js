/* Only trusted local assets run here. All descriptions remain in this process. */
(() => {
  'use strict';
  const worker = new Worker('worker.js');
  function send(data) {
    const message = JSON.stringify(data);
    if (window.chrome?.webview) window.chrome.webview.postMessage(message);
    else if (window.UniShareNative) window.UniShareNative.postMessage(message);
    else window.dispatchEvent(new CustomEvent('unishare-translation', { detail:data }));
  }
  worker.onmessage = ({ data }) => send(data);
  worker.onerror = () => send({ type:'fatal', status:'unavailable' });
  window.uniShareTranslate = request => worker.postMessage(request);
  send({ type:'ready' });
})();
