import { describe, expect, it } from 'vitest';
import { alternarTarea, markdown, progresoTareas } from './markdown';

describe('markdown de las notas', () => {
  it('escapa el HTML: una nota no puede meter etiquetas ni scripts', () => {
    const html = markdown('<img src=x onerror=alert(1)> **hola** <script>');
    expect(html).not.toContain('<img');
    expect(html).not.toContain('<script');
    expect(html).toContain('&lt;img');
    expect(html).toContain('<strong>hola</strong>');
  });

  it('solo enlaza direcciones http, https y mailto', () => {
    expect(markdown('[mal](javascript:alert(1))')).not.toContain('href');
    expect(markdown('[bien](https://ejemplo.com)')).toContain('href="https://ejemplo.com"');
  });

  it('pinta títulos, listas, tareas y código', () => {
    const html = markdown('# Compra\n- [ ] leche\n- [x] pan\n\n1. uno\n\n```\n**no**\n```');
    expect(html).toContain('<h2>Compra</h2>');
    expect(html.match(/class="tarea[ "]/g)).toHaveLength(2);
    expect(html).toContain('tarea hecha');
    expect(html).toContain('<ol><li>uno</li></ol>');
    expect(html).toContain('<pre><code>**no**</code></pre>');
  });

  it('marca la tarea pedida y cuenta las hechas', () => {
    const texto = '- [ ] leche\n```\n- [ ] dentro de código\n```\n- [x] pan';
    expect(alternarTarea(texto, 0)).toContain('- [x] leche');
    expect(alternarTarea(texto, 1)).toContain('- [ ] pan');
    expect(progresoTareas(texto)).toEqual({ total: 2, hechas: 1 });
  });
});
