# 3D Grid System (Village)

Unity 6000.3.11f1 projesi `Village/` klasöründe. Oyun kodu `Village/Assets/_Scripts`, editör kodu `Village/Assets/Editor`.
Kullanıcıyla Türkçe konuşulur; kod yorumları Türkçe. Commit mesajları (GitHub'da görünenler) İngilizce.

## Çalışma şekli
- Büyük değişikliklerden önce tasarım konuşulur; kullanıcı "kodu değiştirme" dediyse sadece anlat.
- Açıklamalar sistem düzeyinde (class/metod isimleriyle) tercih edilir.
- Değişiklik sonrası derleme kontrol edilir; Unity'de test kullanıcıdadır.

## Temel kurallar (koddan kolay çıkmayanlar)
- **Grid:** 1 birim = 1 hücre. Her hücrede iki bağımsız slot var: `Base` ve `Placeable`. Slotlar birbirini bloklamaz (henüz karar verilmedi).
- **Pivot:** Her modelin pivot'u origin hücresinin ortasında. `transform.position` = `OriginWorldPosition` = pivot hücresi; bunu değiştirecek bir şey yapma (drag, Tree, blueprint buna güveniyor).
- **Footprint:** `GridFootprint` hücreleri pivot'a göre offset olarak tutar (negatif olabilir). SO'da katman katman tasarlanır (`GridEntitySOBase.layers`, `layers[0]` zemin).
- **Rotasyon:** Sadece Y ekseninde 90° adımlar. `GridMaskRotator.RotateOffset` ile `ToQuaternion` aynı matematik; görsel ve grid asla ayrışmamalı. Sadece elde tutulan obje döner (R); grid'deki objenin rotasyonu değiştirilmez (`PlacedFootprint` dondurulmuş kopya).
- **Tuşlar:** Q/E kamerayı sola/sağa 90° döndürür (elde obje varken de). R sadece elde tutulan objeyi döndürür. Build mode'da sağ tık mouse'un altındaki objeyi siler (normalde sağ tık = tutma). Q aksiyonu `InputActions.inputactions`'ta; `InputActions.cs` Unity tarafından otomatik üretilir.
- **Taşıma:** `InteractableController` (ne zaman) → `IInteractable` → `GridDragMotor` (nasıl) → `IToolTarget` (hedefte ne olur). Yeni hedef türü = `IToolTarget` uygula.
- **Obje yok edilecekse** `InteractableController.Release` kullan, `HardCancel` değil (o, objeyi grid'e geri koymaya çalışır).

## Odun
- Odunlar **dik** durur. Uzunluk = SO'daki yükseklik (`Size.y`); `Lenghts` enum'u odun için kullanılmıyor (sadece ağaç okları).
- Bağlantı yerleri (`WoodLayout`, `WoodSlot`): tepe → toplam uzunlukta tek parça prefab (`WoodCatalogSO`); dört yan (Forward/Right/Back/Left, odunun kendi yönleri, R ile döner), her yanda katman 1..L yukarıdan aşağı → parça yatırılıp o yanın yönünde uzanır, sonuç `MergedWood`. Alt uç yok.
- Sadece kameraya bakan yüzün sağındaki ve solundaki yanlarda nokta gösterilir (ön ve arka gösterilmez) + tepe.
- Her katman bağımsız doldurulur; hem `Wood` hem `MergedWood` hedeftir (ortak davranış `WoodMergeTarget`, ortak arayüz `IWoodStack.PieceAt(yan, katman)`). Eklenen/taşınan odun her zaman tek parça `Wood`; `MergedWood` başka bir oduna eklenemez. Yatık parçaların kendi katmanları yok.
- Yanları dolu odunun tepesine ekleme: ana odun uzar, dört yanın parçaları yerinde kalır, katman numaraları eklenen uzunluk kadar kayar (`WoodLayout.SidesAfterTopMerge`). Toplam uzunlukta prefab yoksa tepe noktası gösterilmez.
- Blueprint imzası döndürmeden bağımsız (`WoodLayout.CanonicalSignature`: 4 dönüşten sıralamada en küçüğü); döndürülmüş aynı şekil eşleşir, ayna görüntüsü eşleşmez. `steps` çıktısı slot'a yerleştirirken gereken dönüş.
- `MergedWood` kayıt durumu: `baseLength` + 4 yan; eski format (sadece sağ yan, `layers`) yüklenirken sağ yana çevrilir.
- Kombinasyon prefab'ı yapılmaz (eski 2BR_1BR sistemi kaldırıldı).

## Blueprint (boyama kitabı)
- Blueprint sahnesinde (`Scenes/Blueprint.unity`) Play mode'da parçalar yerleştirilir, **Tools → Blueprint Creator** ile prefab + `GridPlaceableSO` üretilir (`Assets/Blueprints`).
- Pivot dünya (0,0,0); builder slot'ları 1 birim aşağı kaydırıyor (kullanıcının ayarı).
- Uyum `IBlueprintPiece.BlueprintSignature` ile (örn. `Wood:2`, `MergedWood:2:0,1,0`). Yeni parça türü (taş, çit) sadece bu interface'i uygular; sistem odun üzerine kurulmamalı.
- Doldurma: `BlueprintSlot` bir `IToolTarget`. Uyan parça (imza eşit) slot'a yapışır ve kendisi doğru yöne döner: hedef rotasyon = blueprint rotasyonu + slot'un `canonicalRotation`'ı + parçanın `BlueprintRotationSteps`'i. Tıklanınca parça yok olur, slot dolar (geri alınamaz). Sıra kuralı yok.
- Boş slot noktalı %30 görünür (`DitherFade`), uyan parça üstündeyken %60; dolunca tam görünür + sallanır. Hepsi dolunca "tamamlandı" efekti (bütünü sallanır + isteğe bağlı partikül). Ödül sonra düşünülecek.
- Dolu slot'lar `Blueprint` `ISaveState` ile kaydedilir (slot sırası). Builder slot'a `canonicalRotation` yazar; builder değişirse blueprint'ler yeniden üretilmeli.

## Ağaç kesme
- `ToolBase`: cooldown + `ToolSwingAnimation`; kullanım vuruş anında olur. Animasyon sadece `VisualTransform`'u döndürür (root motora ait).
- `Tree`: her vuruşta `VfxPool` ile talaş efekti (gövde yüzeyinde, +Z dışarı) ve `HitShake`; ok `maxHealth` bitince kesilir.
- VFX referansları prefab'da tutulur, ortak SO'larda değil. Efektler `VfxPool` ile oynatılır, Instantiate edilmez.

## Görsel stil ve shader'lar
- Stil: yumuşak, kil (clay) gibi yuvarlak modeller, pastel renkler, yumuşak gölgeler, yukarıdan eğik kamera. Hedef platform PC (mobil yok).
- `Assets/Shaders/StylizedWater.shader` (URP HLSL): Depth + Opaque Texture'a ihtiyaç duyar (PC_RPAsset'te açık). Doku kullanmaz, desenler dünya uzayında.
  - Yüzey: iki katman yumuşak Voronoi tepecik (eğim analitik), güneşe bakan yamaç açık + güneş yansıması; hafif şeffaf. Kıyıda temas köpüğü + kıyıya gelen dalga çizgileri (derinliğin eş-değer çizgileri).
  - Köpük/sığ su derinlikten hesaplanır: su bir şeye değmeli. Ada için `IslandSkirt` (base hücrelerinden otomatik eğimli yamaç, `GridManager.BasesChanged` ile yeniden kurulur); su mesh'i `WaterSurface` (sık grid, vertex dalgası için).
  - Bulut gölgeleri: `CloudShadows` (Directional Light'ta) güneşin cookie'sini üretip kaydırır; Lit her şey otomatik gölge alır, su `SampleMainLightCookie` ile okur. Doku geniş (160 birim) ve rastgele dağılımlı ki tekrar fark edilmesin.
  - Materyali elle düzenlerken dikkat: Unity eski shader'ların değerlerini materyalde tutar ve aynı isimli yeni varsayılanları ezer; shader değişince materyali Reset'le.
- Vertex animasyonlu Lit kopyaları: `TreeChopLit.shader` (+ `ChopDent.hlsl`, ağaç materyali) ve `WoodWobbleLit.shader` (+ `Wobble.hlsl`, `New Wood` materyali). URP `Lit.shader`'ın birebir kopyası; sadece 5 pass'in vertex'i sarmalanmış. **Elle düzenlenmez**, `Assets/Shaders/Editor~/make_lit_variants.py` ile üretilir; URP güncellenince bu script tekrar çalıştırılır. Yeni deformasyon = yeni `.hlsl` + script'teki `VARIANTS` listesine bir satır.
- Deformasyon verisini C# (`ChopDent.cs`, `JellyWobble.cs`) MaterialPropertyBlock ile sadece ilgili renderer'lara, object space'te gönderir; animasyonu GPU `_Time.y` ile oynatır, bitince blok temizlenir (SRP Batcher'a geri döner).
- Script fragment de sarabilir (`VARIANTS`'ta `fragment`): `WoodWobbleLit` ayrıca `Fade.hlsl` ile noktalı (dither) saydamlık yapar; ShadowCaster hariç (gölge tam kalır). `DitherFade.cs` aynı MPB yöntemiyle sürer.
- Odun getirilince hedefin kameraya bakan yanındaki parçalar yarı saydam olur (`WoodMergeTarget.FadeFrontPieces`, sadece nokta seçiliyken; çıkışta geri gelir). Ayarı `WoodMerger.frontFade`.
- Odun birleşince (`WoodMerger`) sonuç jöle gibi sallanır: yan birleşmede parçanın geldiği yönün tersine eğilir, tepe birleşmesinde sadece basılıp yaylanır.

## Eldiven imleç (`_Scripts/Cursor`)
- `GloveCursor`: mouse ray'i yüzeye çarpar, avuç ortası (`PalmContact`: Y avucun baktığı yön, Z parmak uçları) çarpılan noktaya oturur, avuç yüzeye bakar; normal etrafındaki dönüş kameraya göre sabit (parmaklar ekranın yukarısına), yumuşak takip. Collider yoksa su seviyesinde düzlem; UI üstünde gizlenir. Blender origin'ine güvenilmez (origin bilekte).
- `GloveFingers` (execution order 100): parmak başına tek serbestlik "kıvrılma"; eksen açılışta (kemik yönü × avuç yönü) ile hesaplanır, bone roll önemsiz. Her kare ikili aramayla "ucu yüzeye değene kadar kıvrıl" (CheckSphere, `fingerRadius`), değmiyorsa sarkar; yaylı geçiş + hafif kıpırdama. `*_Alt` avuç kemikleri hafif çukurlaşır. Animation Rigging paketi yok. "Kemikleri isimden doldur" context menüsü.
- Temas collider'lara göre: görsel kalınlığında collider gerekir (`MergedWood` collider'ları görsel mesh sınırlarından hesaplanır).
- Tutma: normal objelerde eldiven tutma anındaki noktaya objenin yerel uzayında katı bağlanır (SetParent değil: obje birleşmede/slot'ta `Destroy` edilir, eldiven de silinirdi). Aletler `IGloveGrip` (`ToolBase` uygular): eldiven `GloveGrip` noktasının gerçekten child'ı olur (alet silinmez), vuruşta aletle savrulur, parmaklar sabit `GripCurl`'e gider. Yeni alet = `VisualTransform` altına `GloveGrip` + Inspector'da ata.
- Odun hedefleri `IStickyToolTarget`: bir kez yapışınca mouse collider'dan çıksa da kilitli; mouse hedefin kameraya bakan düzleminde ana odun ekseninden `WoodMerger.targetReleaseDistance` uzaklaşınca bırakılır.
- Yapılacak: base grid üstünde ayrı hareket mantığı, durum pozları (önizleme, taşıma, build mode), taşırken kavrama.
- Özel pozlar/hareketler ileride Blender'da: tek kareli `Pose_*` (hedef poz) ve oynayan `Anim_*` action'ları; sadece rotasyon key'lenir.
- Rig (sağ el, 5 parmak, Blender'da dik duruyor → Unity'de düzeltme açısı): kök `Avuç İçi` (ASCII'ye çevrilmesi önerildi); parmaklar `Isaret/Orta/Yuzuk/Serce` × `Alt/Orta/Ust`, başparmak `Bas_Alt/Bas_Ust`. `*_Alt` avuç kemiği sayılır (sadece hafif çukurlaşma), kıvrılma `Orta`+`Ust` ile; başparmakta iki kemik de kıvrılır. Kemikler Inspector'a sürüklenir (isim kuralı yok). Bone roll: dört parmakta yerel X ortak kıvrılma ekseni; kıvrılma işareti açılışta otomatik kalibre edilir.
- FBX: Add Leaf Bones ✓ (parmak ucu = `*_end` kemikleri), Apply Modifiers ✓, sadece Armature + LOW.

## Save sistemi
- Kayıt = dünyayı yeniden kuran tarif: her obje için SO `SaveId` + hücre + rotasyon (+ `ISaveState` ile ekstra durum). JSON, `Application.persistentDataPath/<slot>.json`, güvenli yazma (`SaveStorage`).
- Sahneye elle obje konmaz; her şey build mode ile yerleşir, bu yüzden yükleme boş dünyaya yapılır.
- `SaveManager` sahnede bir tane: oyun sahnesi `save` (her şey), Blueprint sahnesi `blueprint_bases` (sadece base). F5 + çıkışta kaydeder, Start'ta yükler.
- `SaveRegistrySO` (`SOData/SaveRegistry`) tüm entity ve kaynak SO'larını tutar; yeni SO eklenince "Find All Assets". Registry'de olmayan obje kaydedilmez (uyarı verir).
- Her yerleşen objenin prefab + SO'su olmalı; `MergedWood` da (içi boş prefab, görseli `RestoreState`/`Build` kurar).
- Ağacın kesilmiş hali bilerek kaydedilmez (kayıttan tam ağaç çıkar).
