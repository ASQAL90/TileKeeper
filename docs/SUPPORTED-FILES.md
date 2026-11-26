# Jenis file yang didukung TileKeeper

Dokumen ini menjelaskan perilaku file yang ditambahkan lewat **Add…** atau drag-and-drop, ekstensi yang masih bisa didukung, dan batasan yang tidak memungkinkan. Label UI aplikasi tetap bahasa Inggris.

## Perilaku saat ini (1.2.2)

Dialog **Add…** memakai filter:

```text
Shortcut, Application Reference, Remote Desktop, VNC, Executable Files
(*.lnk;*.appref-ms;*.rdp;*.vnc;*.exe)|...|All Files (*.*)|*.*
```

`ResolveProgramFile()` memetakan file sebagai berikut:

| Ekstensi | Hasil | Catatan |
|---|---|---|
| `.lnk` | Target shortcut + argumen | Shortcut ClickOnce diterjemahkan ke perintah `rundll32.exe dfshim.dll,ShOpenVerbApplication ...`. |
| `.appref-ms` | URL deployment ClickOnce | Diambil dari isi file. Parser saat ini masih berasumsi ada `http` dan `#`; file malformed bisa gagal. Diperbaiki di 1.3.0. |
| `.rdp` | `mstsc.exe "path\file.rdp"` | Path ikut jadi argumen, jadi dua file RDP dibedakan sebagai dua identitas. |
| `.vnc` | Viewer terdaftar + argumen dari asosiasi file | Satu-satunya ekstensi non-executable yang sudah memakai asosiasi registry Windows. |
| `.exe` | Program langsung | Bentuk paling stabil. |
| Semua ekstensi lain | Nama file + folder saja | **Belum** bisa dijalankan ulang. `StartProgram()` memotong path pada `.exe` terakhir; tanpa `.exe`, perintah menjadi salah dan hanya masuk log. Penataan juga tidak cocok karena nama file bukan nama process. |

Jadi, jika Anda memilih `laporan.txt`, `foto.png`, atau `video.mp4` lewat **All Files**, baris memang bisa muncul, tetapi:

- tombol watchdog tidak bisa menjalankannya lagi;
- positioning tidak menemukan window yang cocok;
- tidak ada pesan kesalahan di UI — hanya log.

Perilaku ini akan diperbaiki di 1.3.0 dengan resolver asosiasi umum.

## Ekstensi yang masih bisa disupport

Mekanismenya sama seperti `.vnc`: baca asosiasi Windows untuk ekstensi itu (`HKCR\.ext` → `ProgID` → `shell\open\command`), ambil executable + template argumen, ganti placeholder `%1` / `%L` / `%V` dengan path file, lalu simpan hasilnya sebagai `exe + argumen`. File path menjadi bagian dari identitas command line, sehingga dua dokumen berbeda bisa dibedakan.

### 1.3.0 — resolver asosiasi umum

| Grup | Ekstensi | Keterangan |
|---|---|---|
| Teks & web | `.txt`, `.log`, `.md`, `.csv`, `.json`, `.xml`, `.html`, `.htm`, `.url` | Dibuka Notepad/editor/browser. `.url` berisi URL, bukan path lokal. |
| Dokumen | `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt`, `.pptx`, `.rtf`, `.odt`, `.ods`, `.odp` | Dibuka Office/LibreOffice/PDF viewer. Perlu perhatian pada aplikasi single-instance. |
| Gambar | `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.webp`, `.svg`, `.tiff` | Dibuka Photos/IrfanView/Paint, dll. UWP Photos sering cloaked. |
| Video & audio | `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.mp3`, `.wav`, `.flac` | Dibuka VLC/MPC/player lain. Player yang satu instance harus dibedakan lewat judul window. |
| Script | `.bat`, `.cmd`, `.ps1`, `.vbs` | Dibuka `cmd.exe`/`powershell.exe`/`wscript.exe`. Dokumentasikan risiko menjalankan script. |
| Bantuan/konsol | `.chm`, `.msc` | Dibuka help viewer / MMC. Umumnya stabil sebagai window. |

Fallback bila tidak ada asosiasi: minta user memilih executable secara manual, atau tolak penambahan dengan pesan **No application is associated with this file type**. Tidak boleh diam-diam membuat baris yang tidak bisa dijalankan seperti perilaku 1.2.2.

Perbaikan terkait yang mengikutinya:

- `.lnk` yang targetnya dokumen harus di-resolve ulang lewat resolver umum, bukan berhenti di nama file.
- Parser `.appref-ms` diberi guard sebelum `IndexOf("http")` dan `IndexOf("#")`.
- Window Picker + `Class`/`TitleHint` disimpan sebagai pelengkap ID dan argumen, bukan pengganti ID.
- Deteksi window cloaked/elevated ditampilkan sebagai status, bukan kegagalan diam-diam.

### 2.0.0 — model target per window

`WindowTarget = Id + PID + HWND + Class + Title + Args`.

Di sini satu aplikasi boleh punya banyak window target. Grid tidak lagi dibatasi 3×3, UI bisa divirtualisasi, dan aturan positioning inkremental. Ini yang diperlukan jika satu process membuka banyak dokumen.

## Batasan yang tidak memungkinkan / tidak direncanakan

| Kasus | Alasan |
|---|---|
| File tanpa asosiasi dan tanpa executable pilihan user | Windows tidak punya cara deterministik untuk membukanya. |
| `.zip`, `.rar`, `.7z`, `.iso`, `.cab`, `.msi` | Hasilnya archive tool/installer, bukan window aplikasi yang stabil untuk dijaga. Bisa dipertimbangkan manual, bukan otomatis. |
| `.dll`, `.sys`, `.ini`, `.config`, `.tmp` | Bukan entry point GUI. |
| Folder | Dialog Add hanya menerima file. Tidak direncanakan. |
| Child window / panel internal / tab browser | Win32 `SetWindowPos` hanya bekerja masuk akal pada top-level HWND. Tab bukan HWND terpisah. |
| Tool window / popup / modal kecil | `MainWindowHandle` mengabaikan `WS_EX_TOOLWINDOW`; `Match Largest` memilih luas, bukan peran. Perlu Window Picker. |
| Banyak dokumen dalam satu process | Command line process hanya satu; Word/Excel/browser sering membuka dokumen baru ke process yang sudah ada. Tanpa `TitleHint`/HWND, TileKeeper tidak bisa membedakan. |
| UWP/Store (Photos, dll.) | Window bisa cloaked dan `MainWindowHandle` kosong. Bisa dicoba lewat enumerasi, tetapi tidak dijamin. |
| Window elevated dari TileKeeper non-elevated | UIPI menolak `SetWindowPos`. Solusinya menjalankan TileKeeper elevated, dengan konsekuensi sendiri. |
| Game fullscreen / aplikasi yang memaksa geometry sendiri | Aplikasi menimpa posisi setelah ditata. |
| Multi-monitor dengan DPI campuran | Build 1.2.x memakai manifest `dpiAware=true`, bukan Per-Monitor DPI v2. Perlu diuji/diperbaiki di 1.3.0. |
| Ratusan window | `SetWindowPos` + `Thread.Sleep` per window dan ListView non-virtual membuat siklus lambat. Target realistis 1.2.x/1.3.x tetap puluhan window. |

## Keputusan yang dipertahankan

- DACL/process protection tidak dilonggarkan.
- Mutex single-instance tidak diubah.
- Pencocokan argumen RDP/VNC tidak diganti dengan pencocokan nama saja.
- Config lama tetap diterima dan dimigrasikan dengan cadangan.
- UI tetap bahasa Inggris; penjelasan untuk user berbahasa Indonesia.
