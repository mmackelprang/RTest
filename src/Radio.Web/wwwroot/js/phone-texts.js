// §Ph Texts surface — minimal JS interop.
// Scrolls a message-list element to the bottom so the newest bubble is visible
// on conversation open and after a sent/received message. Plain (non-module) so
// JS.InvokeVoidAsync("phoneScrollToBottom", element) resolves it on window.
window.phoneScrollToBottom = el => { if (el) el.scrollTop = el.scrollHeight; };
