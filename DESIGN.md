# Village — Oyun Tasarımı

Oyunun tasarım kararları ve sistemlerin birbirine nasıl bağlandığı. Teknik kurallar `CLAUDE.md`'de, burada "ne ve neden".
Yaklaşım: **önce ana resim, sonra detay.** Bir sistemin içeriğine geçmeden önce diğer sistemlerle bağlantısı burada netleşir.
Konuştukça güncellenir; karar verilenler "Kararlar", verilmeyenler "Açık sorular" altında.

## Tür ve his
- Cozy, grid tabanlı köy kurma / bulmaca. Kil gibi yuvarlak modeller, pastel renkler, minyatür diorama görünümü.
- **Cezasızlık:** enerji / stamina yok, bitki kurumaz-ölmez, kaçırılınca kaybolan süreli etkinlik yok.
- Kancalar olumlu olmalı: sürpriz, tamamlama, görünür ilerleme. Öldürme temalı sistem yok (balık tutma hariç).

## Ana döngü

```
        ┌──────────── yapılar her sisteme işlev katar (genel kurallı, kontrollü) ────────────┐
        ▼                                                                                   │
Yüzen tarla (gemi) ─┐                                                                       │
                    ├─► Yemek pişirme ─► NPC ilişkisi ─► Adanın blueprint'leri açılır ─► İnşa ┘
Balık tutma ────────┘                        │
                                             └─► Tarla parçası ödülü (1×1, 2×1, 2×2...) ─► gemiye takılır

Ada ilerler ─► Sonraki ada (lineer) ─► Yeni malzeme (sınırlı kaynak) ─► Yeni tür yapılar
```

Üç halka:
- **Kısa (her oturum):** ek / tut → pişir → NPC'yi mutlu et.
- **Orta:** ilişki büyür → adada yeni blueprint'ler açılır → inşa et → yapılar diğer sistemlere işlev katar.
- **Uzun:** ada ilerler → sonraki ada, yeni NPC'ler, yeni malzeme.

## Ana sistemler

### Tarım — yüzen tarla
- Tarım **suyun üstünde**, gemiye takılan şekilli tarla parçalarında (1×1, 2×1, 2×2...). Adada inşa alanını kaplamaz.
- Tarla parçaları NPC görevlerinin / ilişkisinin ödülü. Parçaları gemiye yerleştirmek bir şekil bulmacası.
- Gemi istenen adaya / bölgeye götürülebilir. İleride **bölge**: bazı ürünler sadece belli bölgelerde yetişir.
- **Komşuluk:** bitkilerin birbirine karşı ilişkisi var (seven / huysuz). Etki **işlevsel**: ürünün sayısını değil türünü değiştirir. Örnek: domates fesleğen yanında "Aromalı Domates", havuç domates yanında "Eğri Büğrü Havuç". Varyantların hepsi bir işe yarar (bazı tarifler / NPC'ler özellikle onları ister).
- Gösterim: ekmeden önce komşuların üstünde ikonlar (kalp = seven, şimşek = huysuz) + çıkacak ürünün ikonu. Bitkiler büyürken ifade gösterir (sarılır / surat asar). Hiçbiri zarar görmez, vicdan yaptırmaz; huysuzluk komik.

### Balık tutma
- Bot + olta (alet). Yemeğin ikinci malzeme kaynağı.

### Yemek pişirme
- **Ana halkada.** NPC'ler ham ürün yemez, yemek ister (meyve istisna olabilir).
- Tarım gibi işlevsel ve alışılmışın dışında bir mekanik olacak (henüz tasarlanmadı).

### NPC
- Her adada belirli NPC'ler. Sevdiği yemeklerle ilişki artar.
- İlişki şunları açar: o adanın **blueprint'leri** (inşa izni) ve **tarla parçaları**.

### İnşa
- NPC'nin açtığı blueprint'leri doldurmak (şimdiki saydam blueprint sistemi).
- **Yapıların eylemsel karşılığı olmalı**, sadece süs değil. Yapılar bütün sistemleri destekler (tarım, balık, yemek, ulaşım...). Örnek fikirler: iskele → derin suda balık, kuyu → yanındaki tarlayı sular, sera → gece bitkileri, mutfak → yeni tarifler, köprü → adaya bağlanır.
- Bu işlevler **genel kurallara dayalı** olacak (her yapıya özel kod değil), kontrolden çıkmamalı.

### Ada
- **Lineer** ilerleme (şimdilik). Adalar arası etkileşim ileride.
- Her ada: kendi NPC'leri + yeni bir malzeme.

### Malzeme
- Odun var; ilerleyen adalarla taş, demir, ...
- Her malzemenin **işlevsel anlamı** var, sadece görünüşü değişen blok değil. Fikir: odun = hafif, karada; taş = ağırlık taşır, suya dayanır (iskele temeli, kuyu, köprü ayağı); demir = hareket eden şeyler (değirmen, mekanizma).
- Kaynaklar sınırlı (spawn sınırı) → keşif.

## Yardımcı sistemler (park edildi, ana halka oturunca yerleşecek)
- **Kedi** (var): sevgi; fikir: sevgisi yüksekse hediye bırakır.
- **Gün döngüsü** (var): fikir: gece bitkileri.
- **Bot** (var): yüzen tarlanın gemisi olacak.
- **Aletler** (balta var): olta, kazma...
- **Usta puanı / alet upgrade'i:** konuşuldu, sonraya bırakıldı. Fikir: upgrade'ler oynanışı değiştirsin (%10 değil); seçim kalıcı kilit değil, sıralama / alet içi dallanma.
- **Koleksiyon albümü:** boyama kitabı temasıyla; balık, ürün, kedi hediyeleri çıkartma olur.
- **Köy güzelliği (charm):** konuşuldu; ana açma mekanizması NPC ilişkisi oldu, charm'ın yeri sonra belirlenecek.

## Kararlar
1. Ana halka: tarım + balık → yemek → NPC → adanın blueprint'leri → inşa; inşa her sisteme işlev katar.
2. Yemek pişirme ana halkada; NPC'ler ham ürün yemez.
3. NPC ilişkisi o adanın blueprint'lerini ve tarla parçalarını açar.
4. Her ada yeni bir malzeme getirir; kaynaklar sınırlı.
5. Tarım suyun üstünde, gemiye takılan şekilli tarla parçalarıyla.
6. Adalar şimdilik lineer.
7. Yapı işlevleri genel kurallarla, kontrollü.
8. Tarım komşuluğu işlevsel (ürün türünü değiştirir), cezasız.

## Açık sorular
- Yemek pişirmenin mekaniği (işlevsel, farklı).
- Komşuluk kuralının mantığı: mutfak uyumu mu (domates + fesleğen) yoksa renk takımları mı? Renk fikrinde "renge göre dışlama" yanlış okunabilir; takım / rekabet gibi şakacı sunulmalı.
- Yapı işlevlerinin genel kural çerçevesi.
- Malzemelerin işlevleri (taş, demir ve sonrası).
- NPC istekleri sadece yemek mi, eylemler de mi?
- Blueprint (saydam yapı) görünümünün çekici sunumu.
- Charm'ın yeri (var mı, ne açar?).

## Teknik riskler
- **Yüzen tarla = hareket eden grid.** Şu an grid dünyaya sabit. Gemi taşınırken tarla parçaları, üstlerindeki bitkiler ve komşuluklar gemiyle birlikte gitmeli. Tasarımın teknik olarak en büyük işi; detaylara geçmeden önce altyapısı konuşulacak.
