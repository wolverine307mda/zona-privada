namespace Mda.Lanzador.Tests;

public class InformeBateriaTests
{
    private const string Uno = """
        <?xml version="1.0" encoding="UTF-8"?>
        <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012" Version="1.1">
          <ReportInformation><ScanTime>2026-11-04T10:00:00Z</ScanTime></ReportInformation>
          <Batteries>
            <Battery>
              <Id>DELL ABCDE</Id><Manufacturer>SMP</Manufacturer><Chemistry>LION</Chemistry>
              <DesignCapacity>56000</DesignCapacity><FullChargeCapacity>47600</FullChargeCapacity><CycleCount>212</CycleCount>
            </Battery>
          </Batteries>
        </BatteryReport>
        """;

    [Fact]
    public void Lee_la_capacidad_de_diseno_la_actual_y_los_ciclos()
    {
        var d = InformeBateria.Parsear(Uno)!;
        Assert.Equal(56000, d.Diseno);
        Assert.Equal(47600, d.Completa);
        Assert.Equal(212, d.Ciclos);
        Assert.Equal(15.0, d.Desgaste, 1);   // 47600 / 56000 = 85 %: un 15 % de desgaste
    }

    [Fact]
    public void Con_dos_baterias_suma_las_capacidades()
    {
        var xml = "<BatteryReport><Batteries><Battery><DesignCapacity>30000</DesignCapacity><FullChargeCapacity>27000</FullChargeCapacity></Battery>" +
                  "<Battery><DesignCapacity>20000</DesignCapacity><FullChargeCapacity>18000</FullChargeCapacity><CycleCount>40</CycleCount></Battery></Batteries></BatteryReport>";
        var d = InformeBateria.Parsear(xml)!;
        Assert.Equal(50000, d.Diseno); Assert.Equal(45000, d.Completa); Assert.Equal(40, d.Ciclos);
    }

    [Theory]
    [InlineData("")] [InlineData("no es xml")] [InlineData("<BatteryReport><Batteries/></BatteryReport>")]
    [InlineData("<BatteryReport><Batteries><Battery><DesignCapacity>0</DesignCapacity><FullChargeCapacity>0</FullChargeCapacity></Battery></Batteries></BatteryReport>")]
    public void Sin_bateria_o_con_un_informe_roto_no_hay_datos(string xml) => Assert.Null(InformeBateria.Parsear(xml));

    [Fact]
    public void Una_bateria_que_ya_carga_mas_que_la_de_diseno_no_da_desgaste_negativo() =>
        Assert.Equal(0, new DatosBateria(50000, 52000, null).Desgaste);
}
