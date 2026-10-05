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

### Tarım — yüzen tarla + hasat yolu bulmacası
- Tarım **suyun üstünde**, gemiye takılan şekilli tarla parçalarında (1×1, 2×1, 2×2...). Adada inşa alanını kaplamaz.
- Tarla parçaları NPC görevlerinin / ilişkisinin ödülü. Parçalar odun gibi birbirine geçer, **sadece yatayda** birleşir, hepsi 1 birim yüksekliğinde (gerçek tarla gibi düz). R ile döner. Gemi istenen adaya / bölgeye götürülür (ileride bölgeye göre ürün).
- **Hasat = tek çizgi bulmacası (Hamilton yolu):** hasat aletiyle (orak) basılı tutup eldiveni karelerin üstünde sürükleyerek yol çizilir; geçilen kareler hasat edilir. Bulmaca **yerleştirmede**: parçaları öyle dizmeli ki bütün tarla tek yolda, hiçbir kareye iki kez girmeden biter.
- **Ceza yok, kombo var:** kesintisiz yol uzadıkça kombo artar; tarlanın tamamı tek yolda biterse ürünler en kaliteli / bonuslu. Bir kareye tekrar girmek sadece komboyu bitirir, ürün ezilmez, normal kalitede çıkar.
- **Kuralları tarla bloğunun türü belirler, ekin sadece ne çıkacağını.** Her yeni adanın NPC'si yeni bir blok türü verir: bulmaca adalarla derinleşir, her ada bir kural öğretir. Örnek fikirler:
  - normal toprak: bir kez geçilir
  - **çitli:** üç tarafı kapalı, tek taraftan girilir → yolun ya başı ya sonu olmak zorunda (giren geri dönemez)
  - taş döşeli: üstünden düz geçilir, dönülmez
  - köprülü / sulu: iki kez geçilebilir (bir yatay, bir dikey)
  - dönen karo: her hasattan sonra yön değiştirir
- İleride ekinlere bir iki küçük kural eklenebilir (örn. 2 karelik kabak art arda geçilmeli), ama çekirdekte ekinler kuralsız.
- **Hasat koşulları (tekrarı önler, düzene saygılı):** oyuncunun kurduğu düzen bozulmaz; her hasatta bulmacanın **sorusu** değişir. Tarlaya rastgele 1-2 koşul gelir, yerine getirilirse ekstra ödül (nadir malzeme, ekstra kombo, albüm çıkartması). Zorunlu değil, ceza değil; oyuncuyu o an farklı yollar denemeye teşvik eder. Koşul havuzu (adalarla açılır, ilk adada sadece "başla"):
  - **başla:** yol bu blokta başlasın
  - **bitir:** yol bu blokta bitsin (parlak "altın ürün" olarak gösterilebilir)
  - **sıra:** önce A, sonra B bloğundan geç (ileride tarif istekleriyle bağlanır)
  - **ziyaretçi:** yol uyuyan kedinin / konan kuşun karesinden geçsin
  - **tam tur:** bütün tarla tek yolda (temel kombo)
- **Ziyaretçiler engel değil hediye:** kedi bir karede uyur → yol oradan geçerken okşanır (sevgi); kuş konar → geçince ürküp tüy / tohum düşürür. İstenmezse eldivenle dokunup kovulur. Hiçbiri düzeni bozmaz, yolu kapatmaz.
- Bazı dizilimlerde tek yol matematiksel olarak yoktur (satranç boyaması: siyah / beyaz sayısı 1'den fazla farklıysa). Yerleştirirken "tek yolda bitebilir mi" ipucu göstergesi düşünülebilir (ceza değil).

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
8. Tarım bulmacası = hasat yolu (tek çizgi): parçaları tek yolda bitecek şekilde diz, eldivenle yolu çiz.
9. Ceza yok, kombo var (tekrar girmek sadece komboyu bitirir).
10. Yol kurallarını tarla bloğunun türü belirler (her ada yeni blok türü), ekin sadece ürünü belirler. Örnek: çitli blok üç tarafı kapalı → yolun başı ya da sonu.
11. Tarla parçaları sadece yatayda birleşir, hepsi 1 birim yüksek.
12. Tekrarı önleme: her hasatta rastgele **hasat koşulları** (başla / bitir / sıra / ziyaretçi), isteğe bağlı ekstra ödül; oyuncunun düzeni asla bozulmaz. Ziyaretçiler (kedi, kuş) engel değil, yol üstünde hediye; istenirse kovulur.

## Açık sorular
- Yemek pişirmenin mekaniği (işlevsel, farklı).
- Ekim: tek tek ekmek angarya. Fikir: hasat edilen kare otomatik yeniden ekilir (kombo anı tek kalsın) ya da ekim de yol hareketiyle.
- Gemideki tarla alanının sınırı (çizim yormasın), geminin büyütülmesi.
- Yol nereden başlar: serbest mi, geminin iskele kenarından mı (ilk adalarda serbest, sonra sabit giriş olabilir)?
- Ekinlerin büyüme süresi (hepsi aynı anda mı olgunlaşır?).
- Yapı işlevlerinin genel kural çerçevesi.
- Malzemelerin işlevleri (taş, demir ve sonrası).
- NPC istekleri sadece yemek mi, eylemler de mi?
- Blueprint (saydam yapı) görünümünün çekici sunumu.
- Charm'ın yeri (var mı, ne açar?).

## Denenip bırakılanlar
- **Bitki komşuluğu** (seven / huysuz bitkiler, renk takımları, işlevsel varyantlar): oyuncuya fazla hesap yükü, sistem için boşa emek; yerine hasat yolu bulmacası seçildi.

## Teknik riskler
- **Yüzen tarla = hareket eden grid.** Şu an grid dünyaya sabit. Gemi taşınırken tarla parçaları, üstlerindeki bitkiler ve komşuluklar gemiyle birlikte gitmeli. Tasarımın teknik olarak en büyük işi; detaylara geçmeden önce altyapısı konuşulacak.
