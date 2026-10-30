// Sigue el teclado del móvil con visualViewport: deja el diálogo justo encima y
// lleva el campo enfocado a la vista para que no quede tapado.
export function seguirTeclado(dlg: HTMLDialogElement): () => void {
  const vv = window.visualViewport;
  if (!vv) return () => {};
  const ajustar = () => {
    dlg.style.setProperty('--teclado', `${Math.max(0, window.innerHeight - vv.height - vv.offsetTop)}px`);
    dlg.style.setProperty('--alto', `${vv.height}px`);
    const f = document.activeElement;
    if (f instanceof HTMLElement && dlg.contains(f) && f.matches('input, select, textarea')) {
      setTimeout(() => f.scrollIntoView({ block: 'nearest', behavior: 'smooth' }), 60);
    }
  };
  const enfocar = () => setTimeout(ajustar, 300);
  ajustar();
  vv.addEventListener('resize', ajustar);
  vv.addEventListener('scroll', ajustar);
  dlg.addEventListener('focusin', enfocar);
  return () => {
    vv.removeEventListener('resize', ajustar);
    vv.removeEventListener('scroll', ajustar);
    dlg.removeEventListener('focusin', enfocar);
  };
}
