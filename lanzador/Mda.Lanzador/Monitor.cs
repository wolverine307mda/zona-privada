using System.Diagnostics;
using Mda.Api.Sistema;

namespace Mda.Lanzador;

// «Abrir monitor»: el rendimiento real del PC (procesador, memoria, disco, red y los procesos que más gastan) y el registro del lanzador en vivo.
sealed class Monitor : Form
{
    private readonly ILectorSistema _lector = LectorSistema.ParaEsteEquipo();
    private readonly Func<string> _agente;
    private readonly string _registro;
    private readonly System.Windows.Forms.Timer _reloj = new() { Interval = 1500 };
    private readonly ProgressBar _cpu = Barra(), _mem = Barra(), _disco = Barra();
    private readonly Label _tCpu = Dato(), _tMem = Dato(), _tDisco = Dato(), _tRed = Dato(), _tAgente = Dato();
    private readonly ListView _procesos = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(18, 22, 28), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Hora)> _antes = new();
    private int _ciclo;

    public Monitor(string registro, Func<string> estadoAgente)
    {
        _registro = registro; _agente = estadoAgente;
        Text = "Zona privada · monitor"; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(760, 540); Font = new Font("Segoe UI", 9.5f);
        _procesos.Columns.Add("Proceso", 280); _procesos.Columns.Add("CPU", 80, HorizontalAlignment.Right); _procesos.Columns.Add("Memoria", 110, HorizontalAlignment.Right);

        var resumen = new TableLayoutPanel { Dock = DockStyle.Top, Height = 168, ColumnCount = 3, Padding = new Padding(12), RowCount = 5 };
        resumen.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); resumen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resumen.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        void Fila(int i, string nombre, Control barra, Label dato) { resumen.Controls.Add(new Label { Text = nombre, AutoSize = true, Anchor = AnchorStyles.Left }, 0, i); resumen.Controls.Add(barra, 1, i); resumen.Controls.Add(dato, 2, i); }
        Fila(0, "Procesador", _cpu, _tCpu); Fila(1, "Memoria", _mem, _tMem); Fila(2, "Disco", _disco, _tDisco);
        resumen.Controls.Add(new Label { Text = "Red", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3); resumen.Controls.Add(_tRed, 1, 3); resumen.SetColumnSpan(_tRed, 2);
        resumen.Controls.Add(new Label { Text = "Agente", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4); resumen.Controls.Add(_tAgente, 1, 4); resumen.SetColumnSpan(_tAgente, 2);

        var rendimiento = new TabPage("Rendimiento");
        rendimiento.Controls.Add(_procesos);
        rendimiento.Controls.Add(resumen);
        var registroPag = new TabPage("Registro del lanzador") { Padding = new Padding(0) };
        var abrir = new Button { Text = "Abrir la carpeta del registro", Dock = DockStyle.Bottom, Height = 34 };
        abrir.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_registro}\"") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } };
        registroPag.Controls.Add(_log);
        registroPag.Controls.Add(abrir);
        Controls.Add(new TabControl { Dock = DockStyle.Fill, TabPages = { rendimiento, registroPag } });

        _reloj.Tick += (_, _) => Actualizar();
        FormClosed += (_, _) => _reloj.Dispose();
        Actualizar();
        _reloj.Start();
    }

    private static ProgressBar Barra() => new() { Dock = DockStyle.Fill, Maximum = 100, Margin = new Padding(3, 8, 12, 8) };
    private static Label Dato() => new() { AutoSize = true, Anchor = AnchorStyles.Left };

    private void Actualizar()
    {
        try
        {
            var m = _lector.Leer();
            _cpu.Value = (int)Math.Clamp(m.Cpu, 0, 100); _tCpu.Text = $"{m.Cpu:0} % · {m.Nucleos} núcleos";
            var pm = m.MemoriaTotal > 0 ? 100.0 * m.MemoriaUsada / m.MemoriaTotal : 0;
            _mem.Value = (int)Math.Clamp(pm, 0, 100); _tMem.Text = $"{pm:0} % · {m.MemoriaUsada / 1_073_741_824.0:0.0} de {m.MemoriaTotal / 1_073_741_824.0:0.0} GB";
            var pd = m.DiscoTotal > 0 ? 100.0 * m.DiscoUsado / m.DiscoTotal : 0;
            _disco.Value = (int)Math.Clamp(pd, 0, 100); _tDisco.Text = $"{pd:0} % · {(m.DiscoTotal - m.DiscoUsado) / 1_073_741_824.0:0} GB libres";
            _tRed.Text = $"↓ {m.RedBajada / 1_048_576.0:0.0} MB/s   ↑ {m.RedSubida / 1_048_576.0:0.0} MB/s";
            _tAgente.Text = _agente();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { /* una lectura mala no tumba el monitor */ }
        if (_ciclo++ % 2 == 0) ActualizarProcesos();
        if (_ciclo % 2 == 1) ActualizarRegistro();
    }

    // los 12 procesos que más procesador gastan: la CPU de cada uno es lo que ha consumido desde la lectura anterior
    private void ActualizarProcesos()
    {
        var ahora = DateTime.UtcNow; var filas = new List<(string Nombre, double Cpu, long Mem)>();
        var nuevos = new Dictionary<int, (TimeSpan, DateTime)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var t = p.TotalProcessorTime; nuevos[p.Id] = (t, ahora);
                var cpu = _antes.TryGetValue(p.Id, out var a) ? (t - a.Cpu).TotalSeconds / Math.Max(0.1, (ahora - a.Hora).TotalSeconds) / Environment.ProcessorCount * 100 : 0;
                filas.Add((p.ProcessName, cpu, p.WorkingSet64));
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { /* sin permiso o ya cerrado */ }
            finally { p.Dispose(); }
        }
        _antes.Clear(); foreach (var (k, v) in nuevos) _antes[k] = v;
        _procesos.BeginUpdate(); _procesos.Items.Clear();
        foreach (var f in filas.GroupBy(x => x.Nombre).Select(g => (Nombre: g.Key, Cpu: g.Sum(x => x.Cpu), Mem: g.Sum(x => x.Mem))).OrderByDescending(x => x.Cpu).ThenByDescending(x => x.Mem).Take(12))
            _procesos.Items.Add(new ListViewItem([f.Nombre, $"{f.Cpu:0.0} %", $"{f.Mem / 1_048_576.0:0} MB"]));
        _procesos.EndUpdate();
    }

    private void ActualizarRegistro()
    {
        try
        {
            if (!File.Exists(_registro)) { _log.Text = "Aún no hay registro."; return; }
            using var s = new FileStream(_registro, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(s);
            var lineas = r.ReadToEnd().Split('\n').TakeLast(250);
            var texto = string.Join('\n', lineas);
            if (texto == _log.Text) return;
            _log.Text = texto; _log.SelectionStart = _log.TextLength; _log.ScrollToCaret();
        }
        catch (IOException) { /* se está escribiendo */ }
    }
}
