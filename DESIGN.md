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
  - **tam tur:** bütün tarla tek yolda (temel kombo)
- **Ekim ve büyüme döngüsü:** bütün tarla aynı anda ekilir ve aynı anda olgunlaşır, türden bağımsız. Oyuncu o turda istediği ekinleri karelere eker, tarladaki **düğmeye** basar: tarla kapanır, belli bir süre geçer, tarla açılır ve ekilen her şey olgunlaşmıştır. Bulmaca hep tam tarla üzerinde oynanır (farklı büyüme süreleri bulmacayı anlamsızlaştırırdı); ritim oyuncunun elinde.
- **Ekinlerin farkı sadece ne oldukları:** NPC domates istiyorsa domates ekilir. Ekinin yol kuralına etkisi yok (çekirdekte).
- **Gemideki tarla alanı:** hep **tek sayılı kare**: 3×3 → 5×5 → 7×7 (her adada iki büyür). Bot hep tarlanın önünde ve ortasında; çift sayıda orta hücre olmazdı.
- **Tohumlar ücretsiz, seçime dayalı (öneri, onay bekliyor):** tohum harcanan şey değil bilgi; bir ekin türü açılınca sınırsız ekilir. Türleri NPC'ler açar (blueprint ve tarla parçasının yanında). Kıtlığı tarla alanı yaratır (3×3 = 9 kare: "bu tur ne ekeyim?"). Para / dükkân yok. İleride: hasat koşullarının ödülü sınırlı **özel tohumlar** (nadir ekin / varyant).
- **Gemi upgrade'leri** (küçük bir upgrade sistemi): tarla büyüme süresini kısaltma, geminin hızı, "son ekimi hatırla" düğmesi (tek tuşla aynı düzeni tekrar ek).
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
12. Tekrarı önleme: her hasatta rastgele **hasat koşulları** (başla / bitir / sıra), isteğe bağlı ekstra ödül; oyuncunun düzeni asla bozulmaz.
13. Ekim ve büyüme: bütün tarla aynı anda ekilir, düğmeyle kapanır, süre sonunda hepsi birlikte olgunlaşmış açılır.
14. Ekinler sadece ürün olarak farklı (NPC ne isterse o ekilir).
15. Gemideki tarla alanı tek sayılı kare: 3×3 → 5×5 → 7×7; bot önde ortada.
16. Bot + tarla tren gibi: tarla botun izinden gecikmeli gider (aşağıda "Teknik tasarım").
17. Tarla parçaları dünyada placeable (build menüsünden spawnlanır, sürüklenir); botun tarlasına bırakılınca oraya oturur.

## Açık sorular
- Yemek pişirmenin mekaniği (işlevsel, farklı).
- Ekim nasıl yapılır (her kareye tek tek mi, sürükleyerek mi)? "Son ekimi hatırla" ileride gemi upgrade'i.
- Gemi upgrade'leri neyle alınır (henüz para birimi yok)?
- Büyüme süresi ne kadar (gerçek dakika mı, gün döngüsüne bağlı mı)?
- Yol nereden başlar: serbest mi, geminin iskele kenarından mı (ilk adalarda serbest, sonra sabit giriş olabilir)?
- Yapı işlevlerinin genel kural çerçevesi.
- Malzemelerin işlevleri (taş, demir ve sonrası).
- NPC istekleri sadece yemek mi, eylemler de mi?
- Blueprint (saydam yapı) görünümünün çekici sunumu.
- Charm'ın yeri (var mı, ne açar?).

## Teknik tasarım: tarla botu

```
        [ bot 2×1 ]        ← önde ortada, yılan başı + gövdesi (bugünkü gibi)
      ┌───┬───┬───┐
      │   │   │   │        ← tarla: tek sayılı kare, merkezi botun izinden birkaç hücre geride
      ├───┼───┼───┤
      │   │ ● │   │
      ├───┼───┼───┤
      │   │   │   │
      └───┴───┴───┘
```

- **Dünya grid'i** bot + tarlayı tek büyük placeable olarak görür, sadece kapladığı su hücrelerini bilir. **İçeride kim nerede**, botun kendi yerel grid'i (`FarmGrid`) tutar: her karede tarla bloğu + ekin. Hepsi botun child'ı, yerel koordinatta; bot hareket edince kendiliğinden gider, hiçbir hücre taşınmaz. `GridManager` bölünmez (refactor yok).
- **Hareket (tren):** bot bugünkü 2×1 yılan hareketiyle gider; tarlanın merkezi botun izindeki bir noktayı birkaç hücre geriden takip eder (her adımda bir hücre). Köşelerde tarla görsel olarak yumuşakça döner; **tek sayılı kare döndüğünde aynı hücreleri kaplar**, yani dünya grid'inde her adımda sadece bot + tarlanın bir sırası değişir. Gidilecek bütün hücreler su olmalı; değilse gidilmez (büyük tarla dar kanaldan geçemez: rota düşündürür).
- **Çekme halatı:** tarla botun hemen arkasında dursa dönüşlerde köşede botun altına girerdi (iz kıvrılınca); bu yüzden tarla bir halatla çekilir, düzde arada boşluk var (3×3'te 2 hücre, 5×5'te 3). Kısaltılabilir ama köşe çakışması geri gelir.
- **Geri gitme yok** (karar): sadece ileri ve yanlara, U çizerek dönülür. Sıkışınca **limana dön**: bot bütün tarlasıyla ilk konduğu yere ışınlanır (şimdilik H tuşu; ileride UI / dünyada iskele).
- **Tarla parçası:** dünyada `GridPlaceable` (odun gibi sürüklenir, build menüsünden spawnlanabilir). Botun tarlasına bırakılınca (`IToolTarget`, odunun odunla birleşmesi gibi) dünya grid'inden çıkıp `FarmGrid`'e oturur. Şekil + döndürme `GridFootprint` / `GridMaskRotator` ile (R). Eldivenin tarlada base gibi yürümesi için "yürünebilir zemin" tanımı genişletilecek (şu an sadece `GridBase`).
- **Binme:** mouse botun ya da tarlasının üstüne gelince uzaklık sınırı olmadan biner (eldiven adada kıyıda bekler, oradan zıplar).
- **Sert dönüş:** yana basınca bot kıçının etrafında yerinde 90° döner, tarla yerinde kalır (yılan dönüşü köşede takılıyordu).
- **Düzenleme:** bot dururken (dünya butonu ya da UI ile açılan mod).
- **Kayıt:** botun `ISaveState`'i: tarla boyutu, parçalar (tür, yerel konum, dönüş), ekinler, büyüme durumu.
- **Adımlar:** 1) ✅ bot + tarla treni (hareket, dünya grid'i, görsel güverte, çekme halatı, sert dönüş yerinde, limana dön) 2) `FarmGrid` + tarla parçası (dünyadan tarlaya oturma) 3) düzenleme modu 4) ekim 5) büyüme düğmesi 6) hasat yolu + kombo 7) kayıt.

## Sonraya bırakılanlar
- **Tarla ziyaretçileri:** kedi bir karede uyur (yol geçerken okşanır), kuş konar (ürküp tüy / tohum düşürür), eldivenle kovulabilir; engel değil hediye. Ana halka oturduktan sonra.

## Denenip bırakılanlar
- **Bitki komşuluğu** (seven / huysuz bitkiler, renk takımları, işlevsel varyantlar): oyuncuya fazla hesap yükü, sistem için boşa emek; yerine hasat yolu bulmacası seçildi.

## Teknik riskler
- **Yüzen tarla = hareket eden grid.** Şu an grid dünyaya sabit. Gemi taşınırken tarla parçaları, üstlerindeki bitkiler ve komşuluklar gemiyle birlikte gitmeli. Tasarımın teknik olarak en büyük işi; detaylara geçmeden önce altyapısı konuşulacak.
