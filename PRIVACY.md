# Privasi

TileKeeper berjalan lokal di Windows. Tidak ada telemetri, analytics, atau
iklannya, dan aplikasi tidak mengirim data ke server.

## Data yang disimpan di perangkat

| Lokasi | Isi |
| --- | --- |
| `%APPDATA%\TileKeeper\error.log` | log error lokal |
| `<folder aplikasi>\TileKeeper.exe.config` | daftar program yang dipantau dan pengaturan tata letak |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap` | entri host/IP yang ditambahkan untuk alur ClickOnce/Remote Desktop |

TileKeeper juga membaca resolusi monitor dan daftar jendela/proses yang sedang
berjalan untuk menempatkan jendela pada monitor dan sel yang dipilih. Data itu
tidak disimpan di luar konfigurasi tata letak.

## Jaringan

Aplikasi tidak melakukan permintaan jaringan sendiri. Tautan dokumentasi di
aplikasi dibuka melalui browser default. Bila Anda mengaktifkan alur
ClickOnce/Remote Desktop, koneksi dilakukan oleh Windows/komponen terkait, bukan
oleh TileKeeper.

## Menghapus data

- Hapus `%APPDATA%\TileKeeper` dan `TileKeeper.exe.config` setelah aplikasi
  ditutup.
- Hapus entri `ZoneMap` yang tidak lagi dipakai melalui Registry Editor
  (backup terlebih dahulu).

## Perubahan

Perubahan pada dokumen ini dicatat di riwayat Git repositori.
