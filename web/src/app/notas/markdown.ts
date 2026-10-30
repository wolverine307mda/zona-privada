// Markdown mínimo y seguro para las notas: todo el texto se escapa antes de dar formato,
// así que una nota nunca puede meter HTML propio. Las tareas («- [ ]») salen como <span class="tarea">
// en orden, para poder marcarlas por su posición con alternarTarea().

const escapar = (t: string) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const TAREA = /^(\s*[-*+]\s+)\[( |x|X)\]\s?/;

function enLinea(t: string): string {
  // el código va primero y se aparta para que no le afecten los demás formatos
  const codigos: string[] = [];
  let s = t.replace(/`([^`]+)`/g, (_, c) => { codigos.push(c); return `\u0000${codigos.length - 1}\u0000`; });
  s = s
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*(?!\s)(.+?)\*/g, '$1<em>$2</em>')
    .replace(/~~(.+?)~~/g, '<del>$1</del>')
    .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+|mailto:[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
    .replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, '$1<a href="$2" target="_blank" rel="noopener noreferrer">$2</a>');
  return s.replace(/\u0000(\d+)\u0000/g, (_, i) => `<code>${codigos[+i]}</code>`);
}

export function markdown(fuente: string): string {
  const lineas = escapar(fuente.replace(/\r\n?/g, '\n')).split('\n');
  const out: string[] = [];
  let lista: 'ul' | 'ol' | null = null;
  let parrafo: string[] = [];
  let enCodigo = false;
  let codigo: string[] = [];

  const cerrarParrafo = () => { if (parrafo.length) { out.push(`<p>${parrafo.map(enLinea).join('<br>')}</p>`); parrafo = []; } };
  const cerrarLista = () => { if (lista) { out.push(`</${lista}>`); lista = null; } };
  const abrirLista = (t: 'ul' | 'ol') => { if (lista !== t) { cerrarLista(); out.push(`<${t}>`); lista = t; } };

  for (const l of lineas) {
    if (/^```/.test(l.trim())) {
      if (enCodigo) { out.push(`<pre><code>${codigo.join('\n')}</code></pre>`); codigo = []; enCodigo = false; }
      else { cerrarParrafo(); cerrarLista(); enCodigo = true; }
      continue;
    }
    if (enCodigo) { codigo.push(l); continue; }

    const tarea = l.match(TAREA);
    const titulo = l.match(/^(#{1,3})\s+(.*)$/);
    const vineta = l.match(/^\s*[-*+]\s+(.*)$/);
    const numero = l.match(/^\s*\d+[.)]\s+(.*)$/);
    if (!l.trim()) { cerrarParrafo(); cerrarLista(); }
    else if (tarea) {
      cerrarParrafo(); abrirLista('ul');
      const hecha = tarea[2] !== ' ';
      out.push(`<li class="con-tarea"><span class="tarea${hecha ? ' hecha' : ''}"></span><span class="tarea__texto${hecha ? ' hecha' : ''}">${enLinea(l.slice(tarea[0].length))}</span></li>`);
    }
    else if (titulo) { cerrarParrafo(); cerrarLista(); const n = titulo[1].length + 1; out.push(`<h${n}>${enLinea(titulo[2])}</h${n}>`); }
    else if (/^(-{3,}|\*{3,})$/.test(l.trim())) { cerrarParrafo(); cerrarLista(); out.push('<hr>'); }
    else if (l.startsWith('&gt; ')) { cerrarParrafo(); cerrarLista(); out.push(`<blockquote>${enLinea(l.slice(5))}</blockquote>`); }
    else if (vineta) { cerrarParrafo(); abrirLista('ul'); out.push(`<li>${enLinea(vineta[1])}</li>`); }
    else if (numero) { cerrarParrafo(); abrirLista('ol'); out.push(`<li>${enLinea(numero[1])}</li>`); }
    else { cerrarLista(); parrafo.push(l); }
  }
  if (enCodigo) out.push(`<pre><code>${codigo.join('\n')}</code></pre>`);
  cerrarParrafo();
  cerrarLista();
  return out.join('');
}

// Marca o desmarca la tarea número «n» (empezando en 0) del texto
export function alternarTarea(fuente: string, n: number): string {
  let i = -1;
  let enCodigo = false;
  return fuente.split('\n').map((l) => {
    if (/^```/.test(l.trim())) enCodigo = !enCodigo;
    if (enCodigo) return l;
    const m = l.match(TAREA);
    if (!m || ++i !== n) return l;
    return l.replace(TAREA, `${m[1]}[${m[2] === ' ' ? 'x' : ' '}] `);
  }).join('\n');
}

// cuántas tareas hay y cuántas están hechas
export function progresoTareas(fuente: string): { total: number; hechas: number } {
  let total = 0, hechas = 0, enCodigo = false;
  for (const l of fuente.split('\n')) {
    if (/^```/.test(l.trim())) { enCodigo = !enCodigo; continue; }
    const m = !enCodigo && l.match(TAREA);
    if (m) { total++; if (m[2] !== ' ') hechas++; }
  }
  return { total, hechas };
}
