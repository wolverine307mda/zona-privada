using System.Xml.Linq;

// Lo que cuenta el informe de batería de Windows («powercfg /batteryreport /xml»): cuánto cabía cuando era nueva (capacidad de diseño) y cuánto cabe ahora
// (capacidad a plena carga), en mWh, y los ciclos de carga. De ahí sale el desgaste. Sin nada de Windows para poder probarlo.
record DatosBateria(long Diseno, long Completa, int? Ciclos)
{
    // 0 % = como nueva; 18 % = ya solo carga el 82 % de lo que cargaba
    public double Desgaste => Diseno > 0 ? Math.Max(0, 100.0 * (1 - (double)Completa / Diseno)) : 0;
}

static class InformeBateria
{
    // suma las baterías que haya (algunos portátiles llevan dos); null si no hay ninguna o el informe viene vacío
    public static DatosBateria? Parsear(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var baterias = doc.Descendants().Where(e => e.Name.LocalName == "Battery").ToList();
            long diseno = 0, completa = 0; int? ciclos = null;
            foreach (var b in baterias)
            {
                long Num(string nombre) => long.TryParse(b.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value, out var v) ? v : 0;
                diseno += Num("DesignCapacity"); completa += Num("FullChargeCapacity");
                if (ciclos is null && Num("CycleCount") is > 0 and var c) ciclos = (int)c;
            }
            return diseno > 0 && completa > 0 ? new DatosBateria(diseno, completa, ciclos) : null;
        }
        catch (System.Xml.XmlException) { return null; }
    }
}
