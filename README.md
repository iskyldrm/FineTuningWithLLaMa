# FineTuningWithLLaMa

Bu repo, Google Colab uzerinde LoRA ile fine-tune surecini gosteren ornek bir notebook icerir.
Aktif model ornegi: MiniMaxAI/MiniMax-M2.5

## Icerik

- example_colab_finetune_llama.ipynb: Uctan uca ornek egitim akisi

## Notebook Neler Yapiyor?

Notebook su 3 ana hedefi kapsar:

1. Veri yukleme
2. Model egitme
3. Cikti kaydetme

Detayli akis:

1. Colab ortamini hazirlar ve gerekli paketleri kurar.
2. Ornek bir egitim verisi olusturur ve JSONL dosyasi olarak kaydeder.
3. MiniMax-M2.5 modelini 4-bit quantization ile yukler.
4. LoRA ayarlari ile kisa bir fine-tuning yapar.
5. Egitim ciktilarini diske yazar:
	- LoRA adapter dosyalari
	- Tokenizer dosyalari
	- train_metrics.json
	- tinyllama_lora_adapter.zip
6. Sonunda hizli bir inference ornegi calistirir.

## Colab'de Calistirma

1. Bu repoyu klonla veya notebook dosyasini Colab'e yukle.
2. Runtime -> Change runtime type -> GPU sec.
3. example_colab_finetune_llama.ipynb dosyasini bastan sona calistir.

## Cikti Konumlari (Colab)

- /content/output/tinyllama_lora_adapter
- /content/output/train_metrics.json
- /content/output/tinyllama_lora_adapter.zip

## Neden LoRA?

LoRA seciminin temel nedenleri:

1. Colab ortamina daha uygun bellek kullanimi
2. Tam fine-tuning'e gore daha dusuk maliyet ve daha hizli egitim
3. Sadece adapter agirliklarini kaydettigi icin daha kucuk cikti boyutu
4. Deneme ve iterasyon surecini hizlandirmasi

## Modeli Nerde Tutmaliyim?

Egitilen modeli dogrudan repo icine koymak yerine su yaklasim daha sagliklidir:

1. GitHub reposu:
- Kod, notebook, config, metrik ve dokumantasyon
2. Model depolama (onerilen):
- Hugging Face Hub
- Google Drive
- Bulut obje depolama (S3, Azure Blob vb.)

Neden repo icinde buyuk model dosyasi onermiyoruz:

1. Repo hizla sisiyor
2. Clone/pull/push islemleri yavasliyor
3. Git gecmisi gereksiz buyuyor

## Repo Icindeki models/ Klasoru Mantikli mi?

Kisa cevap: sadece kucuk adapter dosyalari icin olabilir, tam model icin onerilmez.

Eger kullanacaksan:

1. Sadece LoRA adapterlarini tut
2. Git LFS kullan
3. Her surum icin net isimlendirme yap (or: v0.1, v0.2)
4. README icinde surum + metrik tablosu tut

## Onerilen Versiyonlama Akisi

1. Her egitim kosusu icin surum etiketi ver
2. train_metrics.json dosyasini sakla
3. Model agirliklarini dis depoya yukle
4. README'de hangi surumun hangi veriyle egitildigini yaz

## MiniMax-M2.5 Notu

Notebook, MiniMaxAI/MiniMax-M2.5 modeline ayarlanmistir.
Bu model Colab GPU tipine gore bellek sinirina takilabilir.
Yetersiz VRAM durumunda:

1. Daha guclu runtime (A100 vb.) kullan
2. Daha kucuk bir temel modele gec
3. Batch/sequence ayarlarini dusur

## Kendi Verinle Denemek

Notebook icindeki sample_rows listesini kendi veri yapina gore degistirebilirsin.
Onerilen alanlar:

- instruction
- input
- output

Bu alanlar otomatik olarak egitim formatina cevrilir.

## Notlar

- Ornek notebook, hizli deneme amacli kisa epoch ve kucuk veri ile gelir.
- Daha iyi sonuc icin veri boyutunu ve egitim suresini artirabilirsin.
"# FineTuningWithLLaMa" 
