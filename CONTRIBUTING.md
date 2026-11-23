# Berkontribusi

## Sebelum mulai

- Masalah keamanan: ikuti `SECURITY.md`, jangan buka issue publik.
- Perubahan besar: buka issue diskusi terlebih dahulu.

## Prasyarat

- Visual Studio 2022 Build Tools atau MSBuild (Windows).
- .NET Framework sesuai target proyek.
- PowerShell 7 untuk skrip rilis.

## Build dan test

```powershell
msbuild src/TileKeeper/TileKeeper.csproj /p:Configuration=Release /p:Platform=AnyCPU /m
msbuild tests/TileKeeper.Tests.csproj /p:Configuration=Release /p:Platform=AnyCPU /m
```

CI saat ini hanya membangun proyek; lihat issue yang ada sebelum menambahkan
ketergantungan baru.

## Aturan commit

- Satu commit = satu perubahan logis.
- Jangan commit artefak build, log, atau kunci.
- Jangan mengubah history branch yang sudah dipublikasikan.

## Checklist pull request

- [ ] Build Release lulus.
- [ ] Tidak ada kredensial atau path mesin pribadi.
- [ ] Perubahan registry didokumentasikan di `PRIVACY.md`.
- [ ] Workflow Actions memakai commit SHA, bukan tag/branch.
- [ ] Perubahan pada perilaku keamanan (proteksi proses, zona Intranet)
      dijelaskan di deskripsi PR.
