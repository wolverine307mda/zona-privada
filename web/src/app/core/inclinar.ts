// Inclinación 3D de las tarjetas con [data-inclina]: siguen al ratón unos pocos grados y una luz
// del color de acento va bajo el puntero. Solo con ratón y si no se ha pedido reducir el movimiento.
export function activarInclinacion(): () => void {
  if (!matchMedia('(hover: hover) and (pointer: fine)').matches || matchMedia('(prefers-reduced-motion: reduce)').matches) return () => {};
  let actual: HTMLElement | null = null;
  let marco = 0;

  const soltar = () => {
    if (!actual) return;
    actual.classList.remove('is-inclinando');
    actual.style.removeProperty('--rx');
    actual.style.removeProperty('--ry');
    actual = null;
  };

  const mover = (e: PointerEvent) => {
    const el = (e.target as Element | null)?.closest<HTMLElement>('[data-inclina]') ?? null;
    if (el !== actual) { soltar(); actual = el; }
    if (!el) return;
    cancelAnimationFrame(marco);
    marco = requestAnimationFrame(() => {
      const r = el.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
      // las tarjetas grandes se inclinan menos: el borde no debe salir disparado
      const max = Math.min(6, 1100 / Math.max(r.width, r.height));
      el.style.setProperty('--ry', `${((x - 0.5) * 2 * max).toFixed(2)}deg`);
      el.style.setProperty('--rx', `${(-(y - 0.5) * 2 * max).toFixed(2)}deg`);
      el.style.setProperty('--lx', `${(x * 100).toFixed(1)}%`);
      el.style.setProperty('--ly', `${(y * 100).toFixed(1)}%`);
      el.classList.add('is-inclinando');
    });
  };

  document.addEventListener('pointermove', mover, { passive: true });
  document.documentElement.addEventListener('pointerleave', soltar);
  return () => {
    document.removeEventListener('pointermove', mover);
    document.documentElement.removeEventListener('pointerleave', soltar);
    soltar();
  };
}
