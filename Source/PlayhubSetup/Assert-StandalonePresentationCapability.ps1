# Read PE exports as data. Never load or invoke a native component to check its capability.
function Assert-StandalonePresentationCapability {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$ExpectedSha256)

    if ($ExpectedSha256 -notmatch '\A[0-9A-Fa-f]{64}\z') { throw 'A complete presenter SHA256 pin is required.' }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $pe = $null
    try {
        if ($stream.Length -lt 512 -or $stream.Length -gt 536870912) { throw 'Presenter PE size is invalid.' }
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) -cne $ExpectedSha256.ToUpperInvariant()) {
            throw 'Presenter bytes changed before capability verification.'
        }
        $stream.Position = 0
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
        $headers = $pe.PEHeaders
        if ($null -eq $headers.PEHeader -or [int]$headers.CoffHeader.Machine -ne 0x8664 -or
            [int]$headers.PEHeader.Magic -ne 0x20b -or ([int]$headers.CoffHeader.Characteristics -band 0x2000) -eq 0) {
            throw 'A native AMD64 DLL is required for the presenter capability.'
        }
        $directory = $headers.PEHeader.ExportTableDirectory
        if ($directory.RelativeVirtualAddress -le 0 -or $directory.Size -lt 40) { throw 'Presenter exports are absent.' }

        function Read-PresenterRva([uint32]$Rva, [int]$Count) {
            if ($Rva -eq 0 -or $Rva -gt [int]::MaxValue -or $Count -le 0) { throw 'Invalid presenter export RVA.' }
            return ,([byte[]]$pe.GetSectionData([int]$Rva).GetContent(0, $Count))
        }
        $table = Read-PresenterRva ([uint32]$directory.RelativeVirtualAddress) 40
        $functions = [BitConverter]::ToUInt32($table, 20)
        $names = [BitConverter]::ToUInt32($table, 24)
        if ($functions -eq 0 -or $functions -gt 4096 -or $names -eq 0 -or $names -gt 4096 -or $names -gt $functions) {
            throw 'Presenter export table is invalid.'
        }
        $addresses = Read-PresenterRva ([BitConverter]::ToUInt32($table, 28)) ([int]$functions * 4)
        $namePointers = Read-PresenterRva ([BitConverter]::ToUInt32($table, 32)) ([int]$names * 4)
        $ordinals = Read-PresenterRva ([BitConverter]::ToUInt32($table, 36)) ([int]$names * 2)
        $expectedName = 'PlayhubPresentationCapabilityV1'
        $expectedValue = [Text.Encoding]::ASCII.GetBytes("playhub-presentation:v1;backend=ordinary-topmost;band=1`0")
        $found = 0
        for ($i = 0; $i -lt $names; $i++) {
            $nameRva = [BitConverter]::ToUInt32($namePointers, $i * 4)
            if ($nameRva -eq 0 -or $nameRva -gt [int]::MaxValue) { throw 'Invalid presenter export name RVA.' }
            $block = $pe.GetSectionData([int]$nameRva)
            $nameBytes = [byte[]]$block.GetContent(0, [Math]::Min(256, $block.Length))
            $end = [Array]::IndexOf($nameBytes, [byte]0)
            if ($end -lt 0) { throw 'Presenter export name is not bounded.' }
            if ([Text.Encoding]::ASCII.GetString($nameBytes, 0, $end) -cne $expectedName) { continue }
            $found++
            $ordinal = [BitConverter]::ToUInt16($ordinals, $i * 2)
            if ($ordinal -ge $functions) { throw 'Presenter capability ordinal is invalid.' }
            $rva = [BitConverter]::ToUInt32($addresses, $ordinal * 4)
            if ([uint64]$rva -ge [uint64]$directory.RelativeVirtualAddress -and
                [uint64]$rva -lt ([uint64]$directory.RelativeVirtualAddress + [uint64]$directory.Size)) {
                throw 'A forwarded presenter capability is not accepted.'
            }
            $sections = @($headers.SectionHeaders | Where-Object {
                [uint64]$rva -ge [uint64]$_.VirtualAddress -and
                ([uint64]$rva + [uint64]$expectedValue.Length) -le ([uint64]$_.VirtualAddress + [uint64]$_.SizeOfRawData)
            })
            if ($sections.Count -ne 1) { throw 'Presenter capability is not backed by one data section.' }
            $flags = [uint64]$sections[0].SectionCharacteristics
            if (($flags -band 0x40000000) -eq 0 -or ($flags -band 0x80000000) -ne 0 -or
                ($flags -band 0x20000000) -ne 0 -or ($flags -band 0x40) -eq 0) {
                throw 'Presenter capability must be initialized, read-only, non-executable data.'
            }
            $value = Read-PresenterRva $rva $expectedValue.Length
            for ($j = 0; $j -lt $expectedValue.Length; $j++) {
                if ($value[$j] -ne $expectedValue[$j]) { throw 'Presenter does not declare the ordinary-topmost band 1 backend.' }
            }
        }
        if ($found -ne 1) { throw 'Exactly one presenter capability data export is required.' }
    }
    finally {
        if ($null -ne $pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}
