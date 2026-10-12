import ltr from 'bootstrap/dist/css/bootstrap.min.css?url';
import rtl from 'bootstrap/dist/css/bootstrap.rtl.min.css?url';

/**
 * Bootstrap ships one style sheet per direction (its own sheets use physical sides). The one that matches the
 * document's dir is linked first in <head>, so the interface's own sheets (index.css, the screens') come after it and
 * win; it follows dir on every change (I18nProvider sets it before paint).
 */
export function linkBootstrap(root: HTMLElement = document.documentElement): () => void {
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.dataset.bootstrap = '';
  document.head.prepend(link);
  const apply = () => {
    const href = root.dir === 'rtl' ? rtl : ltr;
    if (link.getAttribute('href') !== href) link.setAttribute('href', href);
  };
  apply();
  const observer = new MutationObserver(apply);
  observer.observe(root, { attributes: true, attributeFilter: ['dir'] });
  return () => {
    observer.disconnect();
    link.remove();
  };
}
