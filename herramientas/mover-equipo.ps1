# Mueve la zona privada de escritorio a OTRO equipo (el mini PC) sin perder nada: la base de datos, las claves de cifrado, la configuración
# (con la clave maestra de las notas y los secretos) y los ajustes del lanzador. Todo viaja CIFRADO con age: el paquete no sirve sin tu clave privada.
#
#   En el equipo de ahora (pide permiso de administrador: la configuración solo la leen ellos; la zona privada tiene que estar en marcha):
#     .\herramientas\mover-equipo.ps1 -Exportar [-Carpeta D:\traslado]
#
#   En el equipo nuevo (con la zona privada ya instalada: ZonaPrivada.exe --instalar-escritorio) y como administrador. Dos pasadas:
#     .\herramientas\mover-equipo.ps1 -Restaurar -Carpeta D:\traslado -ClavePrivada D:\clave-privada.txt
#       1.ª pasada (la base de datos aún no existe): coloca la configuración y las claves. Después abre la zona privada UNA vez (se crea la base).
#       2.ª pasada (el mismo comando): trae la base de datos, comprueba que cuadra y reinicia la zona privada.
#
# Nunca escribe secretos sin cifrar fuera de una carpeta temporal que borra al acabar. Lo que ya hubiera en el equipo nuevo se guarda antes
# («.antes-del-traslado-…» y una copia de la base en copias\). El paso a paso completo está en docs/14-mover-al-mini-pc.md.
param(
    [switch]$Exportar,
    [switch]$Restaurar,
    [string]$Carpeta,
    [string]$ClavePrivada,
    [switch]$Admin,
    # solo para pruebas: otras rutas y otro puerto de PostgreSQL; -Prueba no pide administrador ni para ni abre la zona privada de verdad
    [switch]$Prueba,
    [int]$PuertoPg = 5433,
    [string]$Datos = (Join-Path $env:ProgramData 'ZonaPrivada'),
    [string]$Instalacion = 'C:\Program Files\ZonaPrivada',
    [string]$ConfigLanzador = (Join-Path $env:APPDATA 'ZonaPrivada\config.json')
)
$ErrorActionPreference = 'Stop'
if (($Exportar -and $Restaurar) -or (-not $Exportar -and -not $Restaurar)) { throw 'Usa -Exportar (en el equipo de ahora) o -Restaurar (en el equipo nuevo), uno de los dos.' }

$puertoPg = $PuertoPg
$pgBin = Join-Path $Instalacion 'componentes\postgres\bin'
$ageExe = Join-Path $Instalacion 'componentes\age\age.exe'
$envRuta = Join-Path $Datos 'config\.env'
$sello = Get-Date -Format 'yyyyMMdd-HHmmss'

function Leer-Env([string]$ruta) {
    $h = @{}
    foreach ($l in (Get-Content -LiteralPath $ruta -Encoding UTF8)) {
        if ($l -match '^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$') { $h[$Matches[1]] = $Matches[2].Trim('"').Trim("'") }
    }
    return $h
}

# Ejecuta un programa y devuelve su código y su salida. Con System.Diagnostics para no mezclar la salida de error con las excepciones de PowerShell 5.1,
# y para pasar la contraseña de PostgreSQL por el entorno del proceso (nunca por la línea de comandos).
function Ejecutar([string]$exe, [string[]]$argumentos, [hashtable]$entorno = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = (($argumentos | ForEach-Object { if ($_ -match '[\s"]' -or $_ -eq '') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' ')
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    foreach ($k in $entorno.Keys) { $psi.EnvironmentVariables[$k] = [string]$entorno[$k] }
    $p = [System.Diagnostics.Process]::Start($psi)
    $err = $p.StandardError.ReadToEndAsync()
    $sal = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    return @{ Codigo = $p.ExitCode; Salida = ($sal + $err.Result).Trim() }
}

function Contar([string]$sql, [hashtable]$entorno) {
    $r = Ejecutar (Join-Path $pgBin 'psql.exe') @('-h', '127.0.0.1', '-p', "$puertoPg", '-U', 'mda', '-d', 'zonaprivada', '-tAc', $sql) $entorno
    $n = 0
    if ($r.Codigo -eq 0 -and [int]::TryParse($r.Salida, [ref]$n)) { return $n }
    return -1
}
$sqlUsuarios = 'select count(*) from "AspNetUsers"'
$sqlTablas = "select count(*) from information_schema.tables where table_schema='public'"

function Carpeta-Temporal {
    $t = Join-Path $env:TEMP ('zp-traslado-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $t | Out-Null
    return $t
}

# ───────────────────────────── EXPORTAR ─────────────────────────────
function Exportar-Equipo {
    if (-not (Test-Path -LiteralPath $envRuta)) { throw "No encuentro la configuración de la zona privada: $envRuta. ¿Está instalada la versión de escritorio en este equipo? (Si no la puedes leer, abre la consola como administrador.)" }
    foreach ($f in @($ageExe, (Join-Path $pgBin 'pg_dump.exe'))) { if (-not (Test-Path -LiteralPath $f)) { throw "Falta $f (la instalación de escritorio está incompleta)." } }
    $cfg = Leer-Env $envRuta
    $clave = $cfg['POSTGRES_PASSWORD']
    $publica = $cfg['COPIAS_AGE_PUBLICA']
    if (-not $clave) { throw 'Falta POSTGRES_PASSWORD en la configuración.' }
    if (-not $publica) { throw 'COPIAS_AGE_PUBLICA está vacía en la configuración: sin una clave pública de age no cifro nada, y el paquete lleva secretos. Ponla (la privada se guarda FUERA del PC) y vuelve a lanzarlo.' }

    if (-not $Carpeta) { $Carpeta = Join-Path ([Environment]::GetFolderPath('Desktop')) "traslado-zona-privada-$sello" }
    if ((Test-Path -LiteralPath $Carpeta) -and (Get-ChildItem -LiteralPath $Carpeta -Force | Select-Object -First 1)) { throw "La carpeta $Carpeta ya existe y no está vacía: elige otra." }
    New-Item -ItemType Directory -Force -Path $Carpeta | Out-Null

    $dia = Get-Date -Format 'yyyyMMdd'
    $entorno = @{ PGPASSWORD = $clave }
    $tmp = Carpeta-Temporal
    try {
        Write-Host 'Volcando la base de datos...' -ForegroundColor Cyan
        $dump = Join-Path $tmp 'base.dump'
        $r = Ejecutar (Join-Path $pgBin 'pg_dump.exe') @('-h', '127.0.0.1', '-p', "$puertoPg", '-U', 'mda', '-d', 'zonaprivada', '-Fc', '-f', $dump) $entorno
        if ($r.Codigo -ne 0 -or -not (Test-Path -LiteralPath $dump) -or (Get-Item -LiteralPath $dump).Length -lt 1024) { throw "pg_dump falló ($($r.Codigo)): $($r.Salida)`n¿Está en marcha la zona privada (el icono junto al reloj)?" }
        $usuarios = Contar $sqlUsuarios $entorno
        $tablas = Contar $sqlTablas $entorno
        if ($usuarios -lt 1 -or $tablas -lt 10) { throw "La base no parece una zona privada completa ($usuarios usuario(s), $tablas tablas): no exporto algo vacío." }

        $version = $null
        try { $version = (Invoke-RestMethod -UseBasicParsing 'http://127.0.0.1:8090/api/salud' -TimeoutSec 5).version } catch { }

        function Cifrar([string]$origen, [string]$nombre) {
            $destino = Join-Path $Carpeta $nombre
            $c = Ejecutar $ageExe @('-r', $publica, '-o', $destino, $origen)
            if ($c.Codigo -ne 0 -or -not (Test-Path -LiteralPath $destino)) { throw "age no pudo cifrar $nombre ($($c.Codigo)): $($c.Salida)" }
            Write-Host ("  {0}  ({1:N1} MB)" -f $nombre, ((Get-Item -LiteralPath $destino).Length / 1MB))
        }

        Write-Host 'Cifrando...' -ForegroundColor Cyan
        Cifrar $dump "zonaprivada-$dia.dump.age"

        $clavesDir = Join-Path $Datos 'claves'
        if (Test-Path -LiteralPath $clavesDir) {
            $tar = Join-Path $tmp 'claves.tar'
            $t = Ejecutar 'tar.exe' @('-cf', $tar, '-C', $clavesDir, '.')
            if ($t.Codigo -ne 0) { throw "No pude empaquetar las claves ($($t.Codigo)): $($t.Salida)" }
            Cifrar $tar "zonaprivada-$dia-claves.tar.age"
        } else { Write-Host '  (no hay carpeta de claves: se sigue sin ella)' -ForegroundColor Yellow }

        Cifrar $envRuta "zonaprivada-$dia-entorno.age"
        if (Test-Path -LiteralPath $ConfigLanzador) { Cifrar $ConfigLanzador "zonaprivada-$dia-lanzador.json.age" }

        # lo que se comprueba al restaurar (no hay nada secreto: son cifras)
        [ordered]@{ fecha = (Get-Date).ToString('s'); dia = $dia; version = $version; usuarios = $usuarios; tablas = $tablas } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Carpeta 'traslado.json') -Encoding UTF8
        @"
Traslado de la zona privada · $dia
Base: $usuarios usuario(s), $tablas tablas. Versión de la API: $(if ($version) { $version } else { 'no se pudo leer' }).

Esto está CIFRADO con age. Para abrirlo hace falta la clave privada (clave-privada.txt), que NO está aquí ni en este PC.

En el equipo nuevo, como administrador y con la zona privada ya instalada:
  .\herramientas\mover-equipo.ps1 -Restaurar -Carpeta "<esta carpeta>" -ClavePrivada "<clave-privada.txt>"
(dos pasadas: la 1.ª coloca la configuración y las claves; abre la zona privada una vez; la 2.ª trae la base).
Guía completa: docs/14-mover-al-mini-pc.md
"@ | Set-Content -LiteralPath (Join-Path $Carpeta 'LEEME.txt') -Encoding UTF8
    } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }

    Write-Host ''
    Write-Host "Paquete listo en: $Carpeta" -ForegroundColor Green
    Write-Host "Base: $usuarios usuario(s), $tablas tablas. Todo va cifrado; la clave privada NO está en el paquete: llévala aparte (gestor de contraseñas o USB)." -ForegroundColor Green
}

# ───────────────────────────── RESTAURAR ─────────────────────────────
function Restaurar-Equipo {
    if (-not $Carpeta -or -not (Test-Path -LiteralPath $Carpeta)) { throw 'Indica la carpeta del paquete con -Carpeta.' }
    if (-not $ClavePrivada -or -not (Test-Path -LiteralPath $ClavePrivada)) { throw 'Indica el archivo de la clave privada de age con -ClavePrivada.' }
    if (-not (Test-Path -LiteralPath (Join-Path $Instalacion 'ZonaPrivada.exe'))) { throw "La zona privada no está instalada en $Instalacion. Instálala antes (ZonaPrivada.exe --instalar-escritorio, como administrador)." }
    foreach ($f in @($ageExe, (Join-Path $pgBin 'pg_restore.exe'))) { if (-not (Test-Path -LiteralPath $f)) { throw "Falta $f (la instalación de escritorio está incompleta)." } }

    # el paquete más reciente de la carpeta
    $dumpAge = Get-ChildItem -LiteralPath $Carpeta -Filter 'zonaprivada-*.dump.age' | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $dumpAge) { throw "No hay ningún zonaprivada-AAAAMMDD.dump.age en $Carpeta." }
    $dia = $dumpAge.Name -replace '^zonaprivada-(\d{8})\.dump\.age$', '$1'
    $archivo = @{
        entorno = Join-Path $Carpeta "zonaprivada-$dia-entorno.age"
        claves = Join-Path $Carpeta "zonaprivada-$dia-claves.tar.age"
        lanzador = Join-Path $Carpeta "zonaprivada-$dia-lanzador.json.age"
        resumen = Join-Path $Carpeta 'traslado.json'
    }
    if (-not (Test-Path -LiteralPath $archivo.entorno)) { throw "Falta $($archivo.entorno): sin la configuración (la clave maestra) las notas y las claves guardadas no se podrían leer." }

    $tmp = Carpeta-Temporal
    try {
        function Descifrar([string]$origen, [string]$destino) {
            $r = Ejecutar $ageExe @('-d', '-i', $ClavePrivada, '-o', $destino, $origen)
            if ($r.Codigo -ne 0) { throw "age no pudo descifrar $(Split-Path $origen -Leaf) ($($r.Codigo)): $($r.Salida)`n¿Es esta la clave privada que corresponde a la pública con la que se cifró?" }
        }
        $envNuevo = Join-Path $tmp 'entorno.env'
        Descifrar $archivo.entorno $envNuevo
        $cfgNuevo = Leer-Env $envNuevo
        if (-not $cfgNuevo['POSTGRES_PASSWORD']) { throw 'La configuración descifrada no tiene POSTGRES_PASSWORD: el paquete no es una configuración de la zona privada.' }

        $iniciada = Test-Path -LiteralPath (Join-Path $Datos 'pg\PG_VERSION')
        if (-not $iniciada) {
            # ── 1.ª pasada: la base aún no existe; la configuración y las claves tienen que estar ANTES del primer arranque (la base se crea con esa contraseña)
            Write-Host '1.ª pasada: configuración y claves (la base de datos aún no existe en este equipo).' -ForegroundColor Cyan
            if (-not $Prueba) { . (Join-Path $PSScriptRoot 'zona-comun.ps1'); Parar-Zona }
            $cfgDir = Join-Path $Datos 'config'
            New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
            if (Test-Path -LiteralPath $envRuta) { Copy-Item -LiteralPath $envRuta -Destination "$envRuta.antes-del-traslado-$sello" -Force; Write-Host "  (la configuración que había se guardó como .env.antes-del-traslado-$sello)" }
            Copy-Item -LiteralPath $envNuevo -Destination $envRuta -Force
            Write-Host '  configuración colocada.'

            if (Test-Path -LiteralPath $archivo.claves) {
                $tar = Join-Path $tmp 'claves.tar'
                Descifrar $archivo.claves $tar
                $clavesDir = Join-Path $Datos 'claves'
                New-Item -ItemType Directory -Force -Path $clavesDir | Out-Null
                $t = Ejecutar 'tar.exe' @('-xf', $tar, '-C', $clavesDir)
                if ($t.Codigo -ne 0) { throw "No pude desempaquetar las claves ($($t.Codigo)): $($t.Salida)" }
                Write-Host '  claves de cifrado colocadas.'
            } else { Write-Host '  (el paquete no trae claves de cifrado: las sesiones y los tokens guardados no valdrán)' -ForegroundColor Yellow }

            if (Test-Path -LiteralPath $archivo.lanzador) {
                $nuevo = Join-Path $tmp 'lanzador.json'
                Descifrar $archivo.lanzador $nuevo
                New-Item -ItemType Directory -Force -Path (Split-Path $ConfigLanzador) | Out-Null
                if (Test-Path -LiteralPath $ConfigLanzador) { Copy-Item -LiteralPath $ConfigLanzador -Destination "$ConfigLanzador.antes-del-traslado-$sello" -Force }
                Copy-Item -LiteralPath $nuevo -Destination $ConfigLanzador -Force
                Write-Host '  ajustes del lanzador colocados.'
            }

            Write-Host ''
            Write-Host 'Hecho. Antes de seguir:' -ForegroundColor Green
            Write-Host '  1. Revisa en C:\ProgramData\ZonaPrivada\config\.env las rutas de este equipo: CARPETA_*, COPIAS_NUBE, COPIAS_DRIVE (las del portátil no valen) y, en el lanzador, la carpeta.'
            Write-Host '  2. Abre la zona privada UNA vez (menú Inicio) y espera a que el icono junto al reloj diga que está en marcha: así se crea la base de datos.'
            Write-Host '  3. Vuelve a lanzar ESTE MISMO comando: la 2.ª pasada trae la base de datos.'
            return
        }

        # ── 2.ª pasada: la base existe (creada con la contraseña de la configuración colocada); se sustituye por la del paquete
        Write-Host '2.ª pasada: base de datos.' -ForegroundColor Cyan
        if (-not (Test-Path -LiteralPath $envRuta)) { throw "No hay configuración en $envRuta." }
        $cfgActual = Leer-Env $envRuta
        if ($cfgActual['POSTGRES_PASSWORD'] -ne $cfgNuevo['POSTGRES_PASSWORD']) {
            throw 'La base de este equipo se creó con OTRA contraseña que la del paquete (se abrió la zona privada antes de la 1.ª pasada). Para empezar limpio: ciérrala, borra C:\ProgramData\ZonaPrivada\pg y vuelve a empezar por la 1.ª pasada.'
        }
        $entorno = @{ PGPASSWORD = $cfgNuevo['POSTGRES_PASSWORD'] }
        $conecta = $false
        try { $c = New-Object System.Net.Sockets.TcpClient; $c.Connect('127.0.0.1', $puertoPg); $conecta = $true; $c.Close() } catch { }
        if (-not $conecta) { throw "PostgreSQL no contesta en el puerto ${puertoPg}: abre la zona privada y espera a que esté en marcha." }

        $dump = Join-Path $tmp 'base.dump'
        Descifrar $dumpAge.FullName $dump
        if ((Get-Item -LiteralPath $dump).Length -lt 1024) { throw 'La base descifrada es demasiado pequeña: el paquete está dañado.' }

        # copia de lo que hay ahora, por si acaso (la base nueva del primer arranque suele estar casi vacía, pero no se pierde nada sin avisar)
        $copias = Join-Path $Datos 'copias'
        New-Item -ItemType Directory -Force -Path $copias | Out-Null
        $antes = Join-Path $copias "antes-del-traslado-$sello.dump"
        $r = Ejecutar (Join-Path $pgBin 'pg_dump.exe') @('-h', '127.0.0.1', '-p', "$puertoPg", '-U', 'mda', '-d', 'zonaprivada', '-Fc', '-f', $antes) $entorno
        if ($r.Codigo -ne 0) { throw "No pude guardar una copia de lo que hay ahora ($($r.Codigo)): $($r.Salida)" }
        Write-Host "  copia de lo que había: $antes"

        $psql = Join-Path $pgBin 'psql.exe'
        $con = @('-h', '127.0.0.1', '-p', "$puertoPg", '-U', 'mda')
        $r = Ejecutar $psql ($con + @('-d', 'postgres', '-qc', 'DROP DATABASE IF EXISTS zonaprivada WITH (FORCE)', '-c', "CREATE DATABASE zonaprivada TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C'")) $entorno
        if ($r.Codigo -ne 0) { throw "No pude preparar la base ($($r.Codigo)): $($r.Salida)" }
        $r = Ejecutar (Join-Path $pgBin 'pg_restore.exe') ($con + @('-d', 'zonaprivada', '--no-owner', '--no-acl', $dump)) $entorno
        if ($r.Codigo -ne 0) { Write-Host "  pg_restore avisó ($($r.Codigo)); suele ser por cosas sin importancia, lo que vale es la comprobación: $($r.Salida)" -ForegroundColor Yellow }

        $usuarios = Contar $sqlUsuarios $entorno
        $tablas = Contar $sqlTablas $entorno
        if (Test-Path -LiteralPath $archivo.resumen) {
            $esperado = Get-Content -LiteralPath $archivo.resumen -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($usuarios -ne $esperado.usuarios -or $tablas -ne $esperado.tablas) {
                throw "La comprobación no cuadra: el paquete tenía $($esperado.usuarios) usuario(s) y $($esperado.tablas) tablas; ahora hay $usuarios y $tablas. Lo que había antes está en $antes."
            }
        } elseif ($usuarios -lt 1 -or $tablas -lt 10) { throw "La comprobación no cuadra ($usuarios usuario(s), $tablas tablas). Lo que había antes está en $antes." }
        Write-Host "  base restaurada: $usuarios usuario(s), $tablas tablas (iguales a las del paquete)." -ForegroundColor Green

        # reiniciar para que la API vuelva a conectarse y cargue las claves
        if (-not $Prueba) {
            . (Join-Path $PSScriptRoot 'zona-comun.ps1')
            Parar-Zona
            Abrir-Lanzador 'tras el traslado'
            $s = Esperar-Salud
            if ($s.Ok) { Write-Host "La zona privada contesta (versión $(if ($s.Version) { $s.Version } else { 'sin dato' }))." -ForegroundColor Green }
            else { Write-Host 'La zona privada aún no contesta: mira el icono junto al reloj y los registros en C:\ProgramData\ZonaPrivada\registros.' -ForegroundColor Yellow }
        }
        Write-Host ''
        Write-Host 'Siguiente: con la zona privada contestando, enciende la salida a Internet (herramientas\instalar-tunel.ps1) DESPUÉS de apagar el túnel del equipo de antes.' -ForegroundColor Cyan
    } finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

# las dos cosas leen o escriben en ProgramData (la configuración solo la leen los administradores): se pide permiso una vez
if (-not $Admin -and -not $Prueba) {
    $esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $esAdmin) {
        $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Admin')
        foreach ($k in $PSBoundParameters.Keys) {
            $v = $PSBoundParameters[$k]
            if ($v -is [System.Management.Automation.SwitchParameter]) { if ($v.IsPresent) { $a += "-$k" } } else { $a += "-$k"; $a += "`"$v`"" }
        }
        try { Start-Process powershell -Verb RunAs -ArgumentList $a -Wait } catch { throw 'Se canceló el permiso de administrador: no se ha hecho nada.' }
        return
    }
}
try { if ($Exportar) { Exportar-Equipo } else { Restaurar-Equipo } }
catch { Write-Host ''; Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; if ($Admin) { Read-Host 'Pulsa Intro para cerrar' | Out-Null }; exit 1 }
if ($Admin) { Read-Host 'Pulsa Intro para cerrar' | Out-Null }
