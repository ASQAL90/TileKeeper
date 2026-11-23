# Privasi

TileKeeper berjalan lokal di Windows. Tidak ada telemetri, analytics, atau
iklannya, dan aplikasi tidak mengirim data ke server.

## Data yang disimpan di perangkat

| Lokasi | Isi |
| --- | --- |
| `%APPDATA%\TileKeeper\error.log` | log error lokal |
| `<folder aplikasi>\TileKeeper.exe.config` | daftar program yang dipantau dan pengaturan tata letak |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\ZoneMap` | entri host/IP privat yang Anda izinkan lewat opsi "Registry — ClickOnce / SmartScreen" (default mati) |

TileKeeper juga membaca resolusi monitor dan daftar jendela/proses yang sedang
berjalan untuk menempatkan jendela pada monitor dan sel yang dipilih. Data itu
tidak disimpan di luar konfigurasi tata letak.

## Jaringan

Aplikasi tidak melakukan permintaan jaringan sendiri. Tautan dokumentasi di
aplikasi dibuka melalui browser default. Bila Anda mengaktifkan alur
ClickOnce/Remote Desktop, koneksi dilakukan oleh Windows/komponen terkait, bukan
oleh TileKeeper.

## Pengecualian SmartScreen (opsional, default mati)

Di tab **System** ada opsi **"Registry — ClickOnce / SmartScreen"**. Opsi ini
hanya perlu dinyalakan bila Anda menjalankan aplikasi ClickOnce dari server
kantor sendiri dan ingin pembaruan otomatisnya tidak memunculkan peringatan
SmartScreen.

- **Default mati.** TileKeeper tidak menulis apa pun ke `ZoneMap` sampai Anda
  mencentang opsi ini dan menekan **Save**.
- **Hanya host privat/intranet.** Alamat loopback, RFC1918 (10/8, 172.16/12,
  192.168/16), link-local, IPv6 ULA/link-local, nama NetBIOS tanpa titik, dan
  akhiran seperti `.local`/`.lan`/`.internal`/`.corp` diterima. Host publik
  (mis. `example.com` atau IP publik) **ditolak** agar perlindungan SmartScreen
  tidak turun diam-diam.
- **Risiko.** Windows akan lebih mempercayai server tersebut. Aktifkan hanya
  untuk server yang Anda kelola sendiri.
- **Daftar terkontrol.** Host yang ditambahkan dicatat di
  `IntranetZoneManagedHosts` pada `TileKeeper.exe.config`, sehingga pembersihan
  hanya menyentuh entri yang dibuat TileKeeper.

## Menghapus data

- Hapus `%APPDATA%\TileKeeper` dan `TileKeeper.exe.config` setelah aplikasi
  ditutup.
- Untuk entri `ZoneMap`: buka tab **System**, klik **"Bersihkan entri
  registry"**, atau matikan opsi lalu tekan **Save** — TileKeeper menghapus
  entri yang ia buat sendiri.
- Entri lain di `ZoneMap` (misalnya yang Anda tambahkan manual) tidak disentuh
  dan dapat dihapus melalui Registry Editor (backup terlebih dahulu).

## Perubahan

Perubahan pada dokumen ini dicatat di riwayat Git repositori.
