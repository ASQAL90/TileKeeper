# Kebijakan Keamanan

## Versi yang didukung

| Versi | Dukungan |
| --- | --- |
| 1.0.x | Perbaikan keamanan dan rilis pemeliharaan |
| < 1.0 | Tidak didukung |

## Melaporkan kerentanan

**Jangan** membuka issue publik untuk masalah keamanan.

1. **GitHub Private Vulnerability Reporting** (disarankan):
   tab **Security → Report a vulnerability**.
2. **Email**: `madinalani@gmail.com` dengan subjek `[SECURITY] TileKeeper`.

Sertakan versi, langkah reproduksi, dan dampak yang Anda perkirakan. Kami
berusaha mengonfirmasi dalam **72 jam** dan memberi pembaruan dalam **7 hari**.

## Lingkup

- Konfigurasi registry yang ditulis aplikasi (mis. zona Intranet).
- Penanganan konfigurasi dan path program yang dijalankan.
- Perlindungan proses dan perilaku tray/startup.
- Pembaruan dan artefak rilis.

## Di luar lingkup

- Penyerang dengan hak Administrator atau akses fisik.
- Malware yang berjalan sebagai pengguna yang sama.
- Kompromi akun GitHub pemilik repositori.
- Lingkungan kiosk yang sengaja dikonfigurasi tanpa kata sandi keluar
  (lihat `docs/USER_GUIDE.md`).

## Model ancaman

TileKeeper berjalan sebagai aplikasi tray per-user tanpa elevasi. Aplikasi
mengubah posisi jendela, dapat menulis entri `HKCU\...\ZoneMap`, dan membaca
konfigurasi di samping executable. Pemilik mesin dan pengguna dengan hak yang
sama dianggap tepercaya; yang dilindungi adalah pengguna non-teknis dari
perubahan tak sengaja dan artefak rilis yang dimodifikasi.

## Safe harbor

Kami menghargai riset dengan itikad baik dan tidak akan menempuh tindakan hukum
selama Anda tidak mengakses data pengguna lain, tidak mengganggu layanan, dan
memberi waktu perbaikan yang wajar.
