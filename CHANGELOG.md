# Riwayat perubahan

## v1.2.2
- Tombol X kembali menyembunyikan jendela ke tray. Jendela juga tidak bisa di-resize atau dimaksimalkan lagi.
- Checkbox Enable dan Screen 1–4 langsung dipakai pada siklus berikutnya, tanpa perlu Save & Close.
- Menambah program di Keep Running otomatis membuat baris pasangannya di Arrange. Setelah itu, Enable dan Remove di kedua daftar berjalan sendiri-sendiri.
- Setiap program punya ID tetap, jadi menambah atau menghapus duplikat tidak membuat baris lain ikut terhapus atau berpindah.
- Opsi Clear this cell (leave empty) untuk mengosongkan satu sel tanpa mengubah bentuk grid.
- Rows dan Cols bisa diatur 1–3 dengan tombol -/+. Grid otomatis membesar kalau jumlah aplikasi tidak muat.
- Grid bisa disusun lebih dulu sebelum ada aplikasi, lalu aplikasi dipilih untuk mengisi sel yang tersedia.
- Reset Programs & Layout sekarang meminta konfirmasi dan mengembalikan pengaturan ke nilai awal.
- Ada peringatan Save / Don't Save / Cancel kalau jendela ditutup ke tray saat masih ada perubahan yang belum disimpan.
- Resolusi layar yang ditampilkan memakai piksel fisik, bukan ukuran yang sudah diskalakan.
- Watchdog mengenali aplikasi yang sudah berjalan sebelum TileKeeper dibuka, termasuk aplikasi ClickOnce, sehingga tidak menjalankannya berulang-ulang.
- Klik dua kali ikon tray cukup sekali untuk membuka jendela, tanpa berkedip atau jendela yang hanya tampil sebagian.
- Nomor sel tidak lagi bergeser saat baris di-uncheck. Baris lain tetap di selnya, dan baris yang di-uncheck menyisakan sel kosong.
- Aplikasi lain tidak lagi melebar mengisi sel milik baris yang di-uncheck.
- Mengklik checkbox tidak lagi mengubah checkbox baris lain yang sedang dipilih untuk Remove.
- Perubahan pin sel dan tombol Rows/Cols langsung terlihat di preview grid.
- Monitor yang tidak terdeteksi menonaktifkan kontrolnya, tetapi tata letak yang tersimpan tidak hilang saat monitor disambung lagi.
- Jendela yang dibuka dari tray tampil penuh dengan sekali klik, tanpa perlu klik kedua.

## v1.1.0
- Checkbox Enable untuk setiap aplikasi, terpisah antara daftar Keep Apps Running dan Arrange Windows.
- Tombol Check for updates dan Open release page di tab Display & Settings. Aplikasi hanya memeriksa rilis terbaru dan membuka halaman unduhan.
- Proteksi proses bisa diatur lewat opsi Process Protection. Default-nya aktif dan perlu restart setelah diubah.
- Antarmuka memakai bahasa Inggris yang lebih konsisten, termasuk nama tab dan tombol Save & Close serta Reset Programs & Layout.
- Karakter & pada label tidak lagi salah tampil.
- Checkbox AutoFit dan Borderless sekarang sejajar dengan sel grid.
- Daftar Screen dan grid map dirapikan agar tidak saling menimpa.
- GitHub Actions dikunci ke commit SHA dan izin token CI dibatasi.
- Kebijakan keamanan, privasi, dan kontribusi ditambahkan.
- Pengecualian SmartScreen di installer sekarang harus dipilih sendiri, tidak aktif diam-diam.

## v1.0.0
- Aplikasi tray untuk Windows yang berjalan di .NET Framework 4.8.
- Keep Running: program atau shortcut yang harus tetap hidup akan dibuka lagi kalau mati.
- Setiap jendela bisa ditempatkan di monitor dan sel grid yang sudah ditentukan. Maksimal 3x3 sel per monitor dan 4 monitor.
- Mode borderless, pencocokan berdasarkan argumen, dan pilihan memakai jendela terbesar.
- Dibuat untuk monitoring SCADA lewat VNC dan RDP, tetapi bisa dipakai untuk aplikasi apa pun yang jendelanya perlu tetap di tempat.
