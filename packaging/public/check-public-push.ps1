#requires -Version 5.1
$ErrorActionPreference = 'Stop'
try {
    $privateRoots = @(git config --get-all unishare.privateRoot)
    if ($LASTEXITCODE -ne 0 -or $privateRoots.Count -eq 0) { throw 'Falta la protección del historial privado.' }
    $approved = git rev-parse --verify 'refs/unishare/approved-public^{commit}'
    if ($LASTEXITCODE -ne 0) { throw 'Primero prepara y revisa una instantánea pública.' }
    foreach ($line in [Console]::In.ReadToEnd().Split([char[]]@("`r", "`n"), [StringSplitOptions]::RemoveEmptyEntries)) {
        $fields = $line.Trim() -split '\s+'
        if ($fields.Count -ne 4) { throw 'Formato de pre-push no válido.' }
        $localRef, $localObject, $remoteRef, $remoteObject = $fields
        $isMain = $localRef -eq 'refs/heads/public' -and $remoteRef -eq 'refs/heads/main'
        $isTag = $localRef -match '^refs/tags/v\d+\.\d+\.\d+$' -and $remoteRef -eq $localRef
        if (-not ($isMain -or $isTag) -or $localObject -eq ('0' * 40)) {
            throw 'Sólo se permite public -> main y etiquetas de versiones públicas; no se permiten borrados.'
        }
        $commit = git rev-parse --verify "$localObject`^{commit}"
        if ($LASTEXITCODE -ne 0) { throw 'El objeto no es una revisión válida.' }
        foreach ($privateRoot in $privateRoots) {
            git merge-base --is-ancestor $privateRoot $commit
            if ($LASTEXITCODE -ne 1) { throw 'Se ha bloqueado historial privado o no se pudo comprobar su procedencia.' }
        }
        if ($isMain -and $commit -ne $approved) { throw 'La rama public cambió después de validar la instantánea.' }
        if ($isTag) {
            git merge-base --is-ancestor $commit $approved
            if ($LASTEXITCODE -ne 0) { throw 'La etiqueta no pertenece al historial público aprobado.' }
            if ($remoteObject -ne ('0' * 40) -and $remoteObject -ne $localObject) {
                throw 'No se permite sustituir una etiqueta publicada.'
            }
        }
        if ($isMain -and $remoteObject -ne ('0' * 40)) {
            git merge-base --is-ancestor $remoteObject $commit
            if ($LASTEXITCODE -ne 0) { throw 'No se permite reescribir main; recupera primero los cambios remotos.' }
        }
    }
}
catch {
    [Console]::Error.WriteLine("Publicación bloqueada: $($_.Exception.Message)")
    exit 1
}
