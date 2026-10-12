namespace Luma.Services;

public static class BrowserScripts
{
    public const string SnapshotScript = """
(() => {
  const visible = (el) => {
    const style = getComputedStyle(el);
    const rect = el.getBoundingClientRect();
    return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
  };

  const esc = (v) => {
    try { return CSS.escape(v); } catch { return v.replace(/[^a-zA-Z0-9_-]/g, '\\$&'); }
  };

  const selectorFor = (el) => {
    if (el.id) return '#' + esc(el.id);
    const testId = el.getAttribute('data-testid');
    if (testId) return `[data-testid="${testId.replace(/"/g, '\\"')}"]`;
    const name = el.getAttribute('name');
    if (name) return `${el.tagName.toLowerCase()}[name="${name.replace(/"/g, '\\"')}"]`;
    const aria = el.getAttribute('aria-label');
    if (aria) return `${el.tagName.toLowerCase()}[aria-label="${aria.replace(/"/g, '\\"')}"]`;

    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && parts.length < 6) {
      let part = node.tagName.toLowerCase();
      const parent = node.parentElement;
      if (parent) {
        const same = [...parent.children].filter((x) => x.tagName === node.tagName);
        if (same.length > 1) part += `:nth-of-type(${same.indexOf(node) + 1})`;
      }
      parts.unshift(part);
      node = parent;
    }
    return parts.join(' > ');
  };

  const interactive = [
    ...document.querySelectorAll('a,button,input,select,textarea,[role="button"],[role="link"],[role="option"],[contenteditable="true"]')
  ].filter(visible).slice(0, 500);

  return interactive.map((el, index) => {
    const type = (el.getAttribute('type') || '').toLowerCase();
    const sensitive = type === 'password' || /cc-|card|cvc|cvv|security-code/i.test(
      `${el.getAttribute('autocomplete') || ''} ${el.getAttribute('name') || ''} ${el.id || ''}`
    );
    const text = sensitive
      ? '[sensitive]'
      : (el.innerText || el.getAttribute('aria-label') || el.getAttribute('placeholder') || '').trim().slice(0, 240);

    return {
      ref: `@${index + 1}`,
      tag: el.tagName.toLowerCase(),
      role: el.getAttribute('role') || '',
      text,
      selector: selectorFor(el),
      disabled: !!el.disabled || el.getAttribute('aria-disabled') === 'true'
    };
  });
})()
""";

    public const string MacroBridgeScript = """
(() => {
  if (window.__lumaMacroBridgeInstalled) return;
  window.__lumaMacroBridgeInstalled = true;

  const esc = (v) => {
    try { return CSS.escape(v); } catch { return v.replace(/[^a-zA-Z0-9_-]/g, '\\$&'); }
  };

  const selectorFor = (el) => {
    if (!el || el.nodeType !== 1) return '';
    if (el.id) return '#' + esc(el.id);
    const testId = el.getAttribute('data-testid');
    if (testId) return `[data-testid="${testId.replace(/"/g, '\\"')}"]`;
    const name = el.getAttribute('name');
    if (name) return `${el.tagName.toLowerCase()}[name="${name.replace(/"/g, '\\"')}"]`;
    const aria = el.getAttribute('aria-label');
    if (aria) return `${el.tagName.toLowerCase()}[aria-label="${aria.replace(/"/g, '\\"')}"]`;
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && parts.length < 6) {
      let part = node.tagName.toLowerCase();
      const parent = node.parentElement;
      if (parent) {
        const same = [...parent.children].filter((x) => x.tagName === node.tagName);
        if (same.length > 1) part += `:nth-of-type(${same.indexOf(node) + 1})`;
      }
      parts.unshift(part);
      node = parent;
    }
    return parts.join(' > ');
  };

  const isSensitive = (el) => {
    const type = (el.getAttribute?.('type') || '').toLowerCase();
    const fingerprint = `${el.getAttribute?.('autocomplete') || ''} ${el.getAttribute?.('name') || ''} ${el.id || ''}`;
    return type === 'password' || /cc-|card|cvc|cvv|security-code|otp|one-time-code/i.test(fingerprint);
  };

  document.addEventListener('click', (event) => {
    if (!event.isTrusted) return;
    const el = event.target?.closest?.('a,button,input,select,textarea,[role="button"],[role="link"],[contenteditable="true"]');
    if (!el) return;
    window.chrome?.webview?.postMessage({
      source: 'luma-macro',
      kind: 'click',
      selector: selectorFor(el)
    });
  }, true);

  document.addEventListener('change', (event) => {
    if (!event.isTrusted) return;
    const el = event.target;
    if (!el || !('value' in el) || isSensitive(el)) return;
    window.chrome?.webview?.postMessage({
      source: 'luma-macro',
      kind: 'input',
      selector: selectorFor(el),
      value: String(el.value).slice(0, 1000)
    });
  }, true);
})()
""";
}
