#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
$stream = $null
$peReader = $null

try {
    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $AssemblyPath).Path)
    $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    if (-not $peReader.HasMetadata) { throw 'Expected a managed baseline assembly.' }

    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
    $assembly = $metadata.GetAssemblyDefinition()
    if ($metadata.GetString($assembly.Name) -ne 'ThermalPilot.Baseline') {
        throw 'Unexpected assembly identity.'
    }

    $entryToken = $peReader.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress
    if (($entryToken -band 0xFF000000) -ne 0x06000000) {
        throw 'Expected a managed method entry point.'
    }

    $entryHandle = [System.Reflection.Metadata.Ecma335.MetadataTokens]::MethodDefinitionHandle($entryToken -band 0x00FFFFFF)
    $entry = $metadata.GetMethodDefinition($entryHandle)
    $entryType = $metadata.GetTypeDefinition($entry.GetDeclaringType())
    if ($metadata.GetString($entry.Name) -ne 'Main' -or
        $metadata.GetString($entryType.Name) -ne 'BaselineEntryPoint' -or
        $metadata.GetString($entryType.Namespace) -ne 'ThermalPilot') {
        throw 'The baseline points at an unexpected startup method.'
    }

    foreach ($typeHandle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($typeHandle)
        $typeName = $metadata.GetString($type.Name)
        if ($typeName -eq '<Module>' -and $type.GetMethods().Count -gt 0) {
            throw 'A module initializer could execute before the inert entry point.'
        }
        if ($typeName -eq 'BaselineEntryPoint' -and $metadata.GetString($type.Namespace) -eq 'ThermalPilot') {
            foreach ($methodHandle in $type.GetMethods()) {
                if ($metadata.GetString($metadata.GetMethodDefinition($methodHandle).Name) -eq '.cctor') {
                    throw 'The baseline entry type has a static initializer.'
                }
            }
        }
    }

    $il = [System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($peReader, $entry.RelativeVirtualAddress).GetILBytes()
    if ($il.Length -eq 0 -or $il[$il.Length - 1] -ne 0x2A) {
        throw 'The baseline entry point does not return immediately.'
    }
    foreach ($instructionByte in $il) {
        if ($instructionByte -ne 0x00 -and $instructionByte -ne 0x2A) {
            throw 'The baseline entry point contains executable work.'
        }
    }

    [pscustomobject]@{
        Assembly = 'ThermalPilot.Baseline'
        EntryPoint = 'ThermalPilot.BaselineEntryPoint.Main'
        Result = 'PASS: inert entry point; no assembly execution performed'
    }
}
finally {
    if ($peReader) { $peReader.Dispose() }
    if ($stream) { $stream.Dispose() }
}
