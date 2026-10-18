# Village — Oyun Tasarımı

Oyunun tasarım kararları ve sistemlerin birbirine nasıl bağlandığı. Teknik kurallar `CLAUDE.md`'de, burada "ne ve neden".
Yaklaşım: **önce ana resim, sonra detay.** Bir sistemin içeriğine geçmeden önce diğer sistemlerle bağlantısı burada netleşir.
Konuştukça güncellenir; karar verilenler "Kararlar", verilmeyenler "Açık sorular" altında.
Sistemlerin görsel haritası: `Docs/system-map.html` (https://claude.ai/artifact/XtrW1tA3empgpHFkahkkcR).

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
- **Görseller:** bütün ekinlerin fidesi aynı model (her CropSO'nun Seedling Model'ine aynı FBX). Türe özel olanlar: tohum kesesi ve olgun bitki. Hangi karede ne ekili olduğu (fide aşamasında) UI ile gösterilecek.
- **Ekinlerin farkı sadece ne oldukları:** NPC domates istiyorsa domates ekilir. Ekinin yol kuralına etkisi yok (çekirdekte).
- **Gemideki tarla alanı:** hep **tek sayılı kare**: 3×3 → 5×5 → 7×7 (her adada iki büyür). Bot hep tarlanın önünde ve ortasında; çift sayıda orta hücre olmazdı.
- **Tohumlar dünyada obje (karar):** tohum adada duran bir placeable (yığın, üstünde sayı). Sağ tıkla tutulur, sol tıkla tarlaya ekilir; sol tık basılı sürüklenince geçilen her boş kareye eker. **Hızlı toplama:** tohum tutarken üstünden geçilen aynı tür tohumlar ele uçup yığına katılır. Tohumların nereden geleceği (NPC, hasat, dükkân) açık. (Eski öneri "tohumlar ücretsiz bilgi" yerine bu seçildi.)
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
18. Tarla her yerden düzenlenir (bot giderken zaten binilmiş, giriş kilitli; ek kural yok).
19. Tohumlar dünyada placeable yığın: sağ tıkla tut, sol tıkla ek (basılı sürükleyerek de); tutarken üstünden geçilen aynı tür tohumlar toplanır.
20. Tarlanın ekinleri ve büyüme durumu kaydedilmez: oyun kapanınca tarla ekinlerden boş açılır.

## Açık sorular
- Yemek pişirmenin mekaniği (işlevsel, farklı).
- Tohumlar nereden gelir (NPC, hasat, dükkân)? "Son ekimi hatırla" ileride gemi upgrade'i.
- Gemi upgrade'leri neyle alınır (henüz para birimi yok)?
- Büyüme süresi ne kadar? Şimdilik gerçek süre (botta ayar, test için 60 sn); gün döngüsüne bağlanması sonra.
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
- **Şu anki durum (2026-10-05):** 1., 2. adım bitti ve test edildi. 3. adım: karar "her yerden düzenlenir" (kod gerekmedi). 4. adım (ekim) bitti, test edildi: eldiven tarlada yürür, tohum placeable (toplama + sürükleyerek ekme), ekin parçanın hücresinde fide olarak durur. 5. adım (büyüme) bitti, test edildi: tarlanın köşesinde düğme, basınca kapak kapanır, süre (üstünde yazar) dolunca açılır, fideler olgun; kapalıyken tarla kilitli, bot gezebilir. 6. adım (hasat yolu) bitti, test edildi: orak, sol tık basılı yol, kombo yazısı, boş kareye girince kombo biter, tam turda her ekin için bir ürün daha (kalite yerine). 7. adım (kayıt) iptal (karar 2026-10-09): ekinler ve büyüme kaydedilmez, oyun kapanınca tarla ekinlerden boş açılır (parçalar, tohum yığınları, envanter kaydedilir). Cila: ekin animasyonları, orak animasyonu, efektler (yer tutucu, modeller gelecek), hasat izi. **Sıradaki:** hasat yolu ipucu (tasarım konuşulacak), etiket UI, sonra madencilik. Çok hücreli parça görseli: şimdilik 1×1 modelin her hücreye kopyası (`tileModel`); ileride istenirse otomatik kenar (ortası kenarsız toprak + dış kenara şerit).
- **Düzenleme:** her yerden, ayrı mod yok (bot sadece binilince gider, binince giriş kilitli).
- **Kayıt:** botun `ISaveState`'i: tarla boyutu, parçalar (tür, yerel konum, dönüş). Ekinler ve büyüme kaydedilmez (karar).
- **Adımlar:** 1) ✅ bot + tarla treni (hareket, dünya grid'i, görsel güverte, çekme halatı, sert dönüş yerinde, limana dön) 2) ✅ `FarmGrid` + tarla parçası (dünyadan tarlaya oturma, tarlada kaydırma, kayıt; Unity'de test edildi, 1×1 parça modeli var) 3) ✅ düzenleme (her yerden, kod yok) 4) ✅ ekim (tohum yığını, toplama, sürükleyerek ekme) 5) ✅ büyüme düğmesi (kapak, süre, olgunlaşma) 6) ✅ hasat yolu + kombo 7) ~~kayıt~~ (iptal: ekinler kaydedilmez).

## Taslak: toprak ve tarla parçası üretimi (2026-10-12, model bekleniyor)
Tarlayı madenciliğe bağlayan halka. Önce gemi modeli (kullanıcı yapıyor), sonra küçükten başlanacak.
- **Toprak:** gemideki depoda duran sayı. Tarla parçaları bununla yapılır. Şimdilik elle ayarlanır; ileride madencilikle artar. Kaydedilir.
- **Blueprint'ler:** tarla parçası şekilleri (1×1, 2×1, L...). Dünyada bulundukça açılır (ileride madenden, NPC'den). Şekil sayısını tasarım belirler.
- **Çalışma masası:** geminin arkasında. Tıklanınca ayrı bir **yapım platformu** açılır, UI yok, her şey dünyada: toprak miktarı yazar, açılmış blueprint'ler durur, biri seçilir, şekli saydam taslak olarak belirir.
- **Yapım:** oyuncu taslağın hücrelerine sol tıklayarak toprak doldurur (her hücre topraktan düşer, dolunca toprak olur). Hepsi dolunca parça hazır. Oyun / zorluk yok; amaç "kendim yapıyorum" hissi.
- **Gemi:** bot büyük gemi olacak. Parçaları: gövde, toprak deposu (toprak yığını doluluğa göre yükselebilir), çalışma masası, tarla bağlantı noktası; ileride upgrade masası ve borulu matkap.
- **Maliyet:** hücre başına sabit toprak; parçanın maliyeti = hücre sayısı × o değer.
- **Platform:** masaya basınca masa ters döner, geminin en üstünde düz bir 3×3 grid açılır (bugünkü grid gibi). Üstünde seçili blueprint'in taslağı, yanında toprak miktarını gösteren depo.
- **Biten parça:** platformun üstünde kalır, oyuncu alana kadar orada durur. O sırada gemi ve başka şeyler kullanılamaz.
- **Doldurma:** her hücreye ayrı tık, çok kısa (bir saniyeden az). Amaç tatmin: doldurma / tamamlama hissi.
- **Bilinen sorun:** gemi ile adalar arasındaki geçiş karışık; oyuncu eldivenin gemide mi adada mı olduğunu anlamıyor. Geçiş sistemi sonra (cila).
- **Bilinen sorunlar (2026-10-17, kullanıcı):** tarla sistemi büyük bir refactor istiyor: parça yerleştirmesi stabil değil, bazı parçaların tutma kutuları (box collider) sorunlu. Temel atıldı, refactor sonra.
- **Güncelleme (2026-10-13):** gemi 3×5 oldu. Çalışma masası gemide değil, adada ayrı 3×3 placeable (taşınabilir); üstündeki 3×3 tepside gerçek boyutta yapılır. Blueprint = tarla parçasının SO'su (şekil oradan). Seçim şimdilik basit ekran UI'ı (sonra değiştirilebilir). Toprak şimdilik gemiden otomatik çekilir; ileride matkap / masa kablo (verlet) ile bağlanabilir.

## Taslak: derin build sistemi (2026-10-17, konuşuldu, kod yok)
Sorun: bugünkü grid her hücrede tek placeable varsayıyor; yarım odun, süs eşyası (ışık, bank, pencere) konamıyor. Üç katman:
1. **Kaba grid** (bugünkü 1×1): zemin, büyük yapılar, gemi, tarla. Diğer iki katmanın taşıyıcısı.
2. **İnce grid:** her hücre 2×2×2 = 8 küçük küpe bölünür (0.5 birim; bölme sayısı ayar, ileride 4'e çıkabilir). Her hücrede doluluk maskesi (8 bit). Obje kapladığı küçük küpleri söyler: tam odun 8, yarım odun 4, çeyrek eşya 1-2, yassı eşya alt kat. Mouse yüzeyin 4 çeyreğinden birini gösterir, eşya boyuna göre yerleşir (yarım odunun yönü R ile). Bugünkü objeler "tam dolu" sayılır, sistem bozulmaz. Yerden / bir şeyin üstüne konanlar için.
3. **Yuvalar (socket):** duvara / tavana / direğe takılanlar (pencere, kapı, lamba, raf, çatı). Parça kendi yuvalarını tanımlar (tür: yüz, yan, üst...), eşya hangi türe takılacağını söyler; elde tutarken uyan yuvalar parlar. Süs takıldığı parçaya bağlı yaşar (taşınır, kaydedilir: parça + yuva). Odun birleştirmesi (WoodSlot) zaten bir yuva sistemi: genelleştirilecek.
- **Sıra:** ince grid altyapısı (maske) → yarım odun → yuva sistemi (odundan genelleştir) + pencere → serbest küçük eşyalar.
- **Açık:** süslerin dönüşü (90° mi 45° mi), yuva mı serbest mi (öneri: yuva).
- **Karar (2026-10-17):** build sistemi 2'lik ince grid. Yapının iskeleti (duvar, zemin, direk, çatı) elle kurulur; küçük dekorasyonlar (lamba, bank, pencere...) üretim masalarında ("printer") yapılıp yapının üstüne / yuvalarına yerleştirilir. Dekorasyonlar da diğer placeable'lar gibi sonradan tutulup grid'de / yuvalar arasında istenen yere taşınabilmeli.
- **Üretim masaları:** farklı masalar olacak (ör. marangoz tezgahı, cam fırını; adalarla açılabilir). Üretim: gerekli malzemeler masanın üstüne konur, masa eşyayı verir (detayı sonra).
- **Madencilikle ortak:** aynı 8 bitlik maske madende "kazılmamış küçük küp" demek; matkap değdiği küçük küpü kazar, maske 0 olunca blok kırılır. Tek sistem, iki kullanım.

## Taslak: craft malzeme zincirleri (2026-10-18, kod yok)
Craft sisteminin girdisi olan malzemeler (ürün değil). Her aile: doğadan gelen ham madde → işlendikçe daha ince / özel malzeme. Odun ve taş bölünerek (tam → yarım → çeyrek → levha), kil / cam / demir fırında ya da eritilerek dönüşür. Boyutlar sonra (kalas 0.5 kalınlık, ince grid'de 4 küp olabilir).
- **Odun** (ada 1, balta): Ağaç → Odun → Yarım odun → Çeyrek odun → Kalas → Çıta. (Odun ve yarım odun oyunda var; ana zincir kullanıcıca uygun bulundu.)
  - Odundan yan dallar (sadece fikir, **karar verilmedi**):
    - Çeyrekten: Çeyrek odun → Sırık → Çubuk → Ahşap çivi
    - Dal: Ağaç → Dal → Çalı çırpı → Sepet örgüsü
    - Kabuk: Odun → Kabuk → Kabuk lifi → Kaba ip
    - Reçine: Ağaç → Reçine → Zift → Vernik (suya dayanıklılık)
    - Talaş: kesme / yontma artığı → Talaş → Kompost
    - Kömür: Odun → Odun kömürü → Kül (fırın yakıtı: tuğla, cam, demir)
    - Tohum: Ağaç → Kozalak / tohum → Fidan → Ağaç (yeniden dikim)
- **Lif** (tarla, orak): Lif ekini → Lif → İp → Kumaş
- **Toprak** (gemi, var): Toprak → Kil → Tuğla → Kiremit
- **Taş** (ada 2, kazma / matkap): Kaya → Taş blok → Yarım taş → Kesme taş → Taş levha
- **Kum** (kıyı, kürek): Kum → Cam → Cam levha
- **Demir** (ada 3, maden): Cevher → Külçe → Demir çubuk → Levha → Çivi
- **Odunun işlenmesi (karar, 2026-10-18):**
  - Odun → Yarım odun → Çeyrek odun: **kesme kütüğünde balta** ile. Odun kesme kütüğünün üstünde olmalı, öyle vurulur.
  - Kalas ve Çıta (işlenmiş ürünler): **testere sehpası** gibi bir masada. Etkileşim testereyi elle ileri geri çekmek gibi; detayı sonra yazılacak.
  - Otomasyon (su değirmeni bıçkı, toplu işleme): ileride, henüz erken.
- Gösterim: kullanıcı bu zincir biçimini (yan yana kutular, oklarla) beğendi; malzeme listeleri böyle gösterilecek.

## Sonraya bırakılanlar
- **Tarla ziyaretçileri:** kedi bir karede uyur (yol geçerken okşanır), kuş konar (ürküp tüy / tohum düşürür), eldivenle kovulabilir; engel değil hediye. Ana halka oturduktan sonra.

## Denenip bırakılanlar
- **Bitki komşuluğu** (seven / huysuz bitkiler, renk takımları, işlevsel varyantlar): oyuncuya fazla hesap yükü, sistem için boşa emek; yerine hasat yolu bulmacası seçildi.

## Teknik riskler
- **Yüzen tarla = hareket eden grid.** Şu an grid dünyaya sabit. Gemi taşınırken tarla parçaları, üstlerindeki bitkiler ve komşuluklar gemiyle birlikte gitmeli. Tasarımın teknik olarak en büyük işi; detaylara geçmeden önce altyapısı konuşulacak.
