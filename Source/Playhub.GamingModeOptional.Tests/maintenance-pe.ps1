$assembly = Join-Path $PSScriptRoot '..\Playhub\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Playhub.dll'
$stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($assembly))
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $found = $false
    foreach ($handle in $reader.TypeDefinitions) {
        $type = $reader.GetTypeDefinition($handle)
        $name = $reader.GetString($type.Name)
        $namespace = $reader.GetString($type.Namespace)
        if ($namespace.StartsWith('Playhub.Emulation')) { throw "Emulation type shipped: $namespace.$name" }
        if ($name -eq 'BuildFeatures') {
            foreach ($fieldHandle in $type.GetFields()) {
                $field = $reader.GetFieldDefinition($fieldHandle)
                if ($reader.GetString($field.Name) -eq 'EmulationEnabled') {
                    $constant = $reader.GetConstant($field.GetDefaultValue())
                    if ($reader.GetBlobBytes($constant.Value)[0] -ne 0) { throw 'Emulation flag is enabled' }
                    $found = $true
                }
            }
        }
    }
    if (-not $found) { throw 'Feature flag missing' }
    'PASS maintenance PE: EmulationEnabled=false, no Playhub.Emulation types'
} finally { $pe.Dispose(); $stream.Dispose() }
