using System.Text;

namespace Mda.Lanzador;

// Primer arranque sin tocar el .env a mano: si falta algo, inventa las claves que se pueden inventar (base de datos, token del agente, clave maestra de cifrado),
// pide el correo y la contraseña del administrador y enseña la clave maestra para guardarla fuera del PC (sin ella, las notas y claves cifradas no se pueden leer).
static class Asistente
{
    // true si el .env queda completo; false si se cancela
    public static bool Completar(string carpeta, Action<string> log)
    {
        var ruta = Path.Combine(carpeta, ".env");
        var ejemplo = Path.Combine(carpeta, ".env.example");
        var contenido = File.Exists(ruta) ? File.ReadAllText(ruta) : File.Exists(ejemplo) ? File.ReadAllText(ejemplo) : "";
        var faltan = Primera.Faltan(Entorno.LeerEnv(contenido.Replace("\r\n", "\n").Split('\n')));
        if (faltan.Count == 0) return true;

        var nuevos = new Dictionary<string, string>();
        foreach (var k in faltan.Where(Primera.Generables.Contains)) nuevos[k] = Primera.Generar(k);
        if (faltan.Contains("ADMIN_EMAIL") || faltan.Contains("ADMIN_PASSWORD"))
        {
            using var f = new PedirAdmin();
            if (f.ShowDialog() != DialogResult.OK) return false;
            nuevos["ADMIN_EMAIL"] = f.Correo; nuevos["ADMIN_PASSWORD"] = f.Clave;
        }

        if (File.Exists(ruta)) File.Copy(ruta, Path.Combine(carpeta, $".env.copia-{DateTime.Now:yyyyMMdd-HHmmss}"), true); // por si acaso (está en .gitignore)
        File.WriteAllText(ruta, Primera.Completar(contenido, nuevos));
        log($"Asistente: completado el .env con {string.Join(", ", nuevos.Keys)}");

        if (nuevos.TryGetValue("MDA_CLAVE_MAESTRA", out var maestra)) { using var m = new MostrarClave(maestra); m.ShowDialog(); }
        return true;
    }

    // ---- pide el correo y la contraseña del administrador ----
    sealed class PedirAdmin : Form
    {
        private readonly TextBox _correo = new() { Dock = DockStyle.Top };
        private readonly TextBox _clave = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
        private readonly TextBox _repite = new() { Dock = DockStyle.Top, UseSystemPasswordChar = true };
        private readonly Label _error = new() { Dock = DockStyle.Top, ForeColor = Color.Firebrick, AutoSize = false, Height = 36 };
        public string Correo => _correo.Text.Trim();
        public string Clave => _clave.Text;

        public PedirAdmin()
        {
            Text = "Zona privada · primer arranque"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ClientSize = new Size(420, 330); Padding = new Padding(16); Font = new Font("Segoe UI", 9.5f);
            var ok = new Button { Text = "Crear y arrancar", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.None };
            ok.Click += (_, _) =>
            {
                if (!Primera.CorreoValido(_correo.Text)) { _error.Text = "Escribe un correo válido."; return; }
                if (!Primera.ClaveAdminValida(_clave.Text)) { _error.Text = "La contraseña necesita al menos 12 caracteres (mejor una frase larga)."; return; }
                if (_clave.Text != _repite.Text) { _error.Text = "Las dos contraseñas no coinciden."; return; }
                DialogResult = DialogResult.OK;
            };
            AcceptButton = ok;
            // se añaden de abajo arriba: Dock = Top apila en el orden inverso
            Controls.AddRange([ok, _error, _repite, Rotulo("Repite la contraseña"), _clave, Rotulo("Contraseña (mínimo 12 caracteres)"), _correo, Rotulo("Tu correo (con él entras)"),
                new Label { Text = "Primera vez en este equipo: el resto de claves las invento yo.", Dock = DockStyle.Top, Height = 30 }]);
        }

        private static Label Rotulo(string t) => new() { Text = t, Dock = DockStyle.Top, Height = 24, Padding = new Padding(0, 8, 0, 0) };
    }

    // ---- enseña la clave maestra una vez ----
    sealed class MostrarClave : Form
    {
        public MostrarClave(string clave)
        {
            Text = "Guarda esta clave fuera del PC"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ClientSize = new Size(520, 270); Padding = new Padding(16); Font = new Font("Segoe UI", 9.5f);
            var texto = new Label { Dock = DockStyle.Top, Height = 78, Text = "Esta es la clave maestra: cifra las notas, el portapapeles y las claves de servicios en la base de datos.\r\nSi la pierdes, esos datos no se pueden recuperar, ni con una copia. Guárdala en tu gestor de contraseñas." };
            var caja = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = clave, Font = new Font("Consolas", 10.5f) };
            var copiar = new Button { Text = "Copiar", Width = 110, Height = 34 };
            copiar.Click += (_, _) => { Clipboard.SetText(clave); copiar.Text = "✓ Copiada"; };
            var guardar = new Button { Text = "Guardar en un archivo…", Width = 180, Height = 34 };
            guardar.Click += (_, _) =>
            {
                using var d = new SaveFileDialog { FileName = "clave-maestra-zona-privada.txt", Filter = "Texto|*.txt", Title = "Guarda el archivo fuera de este PC (un USB, por ejemplo)" };
                if (d.ShowDialog() == DialogResult.OK) File.WriteAllText(d.FileName, clave + Environment.NewLine, Encoding.UTF8);
            };
            var visto = new CheckBox { Text = "Ya la he guardado fuera del PC", Dock = DockStyle.Top, Height = 34 };
            var cerrar = new Button { Text = "Continuar", Dock = DockStyle.Bottom, Height = 38, Enabled = false, DialogResult = DialogResult.OK };
            visto.CheckedChanged += (_, _) => cerrar.Enabled = visto.Checked;
            var botones = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 8, 0, 0) };
            botones.Controls.AddRange([copiar, guardar]);
            ControlBox = false;
            Controls.AddRange([cerrar, visto, botones, caja, texto]);
        }
    }
}
