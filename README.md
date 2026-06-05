# Parallel Space Wave Simulator

Project ini dibuat untuk Evaluasi 3 mata kuliah **IFB 206 Komputasi Paralel**.

## Deskripsi

Parallel Space Wave Simulator adalah prototype game top-down shooter sederhana menggunakan **C# WinForms**. Program menampilkan player, enemy, dan projectile sebagai visualisasi game. Fokus utama project bukan membuat game lengkap, tetapi menunjukkan konsep **komputasi paralel** melalui simulasi update banyak enemy.

## Konsep Komputasi Paralel

Project ini menggunakan konsep **Data Parallelism**.

Dalam game, setiap enemy memiliki posisi, HP, defense, dan status hidup/mati. Karena update satu enemy tidak bergantung langsung pada enemy lain, data enemy dapat dibagi dan diproses secara bersamaan.

Mode yang dibandingkan:

1. **Serial**: semua enemy diproses satu per satu menggunakan `for-loop`.
2. **Parallel**: semua enemy diproses secara paralel menggunakan `Parallel.For`.

Program menampilkan waktu eksekusi serial, waktu eksekusi paralel, jumlah enemy yang kalah, dan speedup.

## Struktur Folder

```text
Parrarel-Game/
├── Assets/
│   ├── Player.png
│   ├── Enemy.png
│   └── Projectile.png
├── Program.cs
├── ParallelGame.csproj
├── README.md
├── index.html
└── style.css
```

## Cara Menjalankan

Pastikan sudah menginstall .NET SDK.

```bash
dotnet run
```

Kontrol:

```text
WASD / Arrow  : gerak player
Hold Space    : auto shoot / tembak terus-menerus
B             : benchmark serial vs parallel
1             : benchmark serial saja
2             : benchmark parallel saja
R             : reset / restart setelah game over
```

## Teknologi

- C#
- .NET Windows Forms
- `System.Threading.Tasks.Parallel.For`
- GitHub Pages untuk dokumentasi

## Hasil yang Ditampilkan

- Visualisasi game sederhana
- Auto shoot saat tombol Space ditahan
- Game over ketika player overlap/collide dengan enemy
- Serial execution time
- Parallel execution time
- Speedup
- Jumlah enemy defeated/alive

## Catatan

Jumlah enemy yang divisualkan dibuat sedikit agar rendering tetap ringan. Untuk benchmark komputasi paralel, program mensimulasikan 1.000.000 enemy secara data-only.


## Runtime Mode

- Z: Serial runtime mode (visual update menggunakan for-loop).
- X: Parallel runtime mode (visual update menggunakan Parallel.For).
- Overlay menampilkan FPS, frame time, runtime simulation time, Serial Time, Parallel Time, dan Speedup.
