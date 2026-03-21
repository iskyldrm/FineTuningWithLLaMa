# FineTuningWithLLaMa

Bu repo, Google Colab uzerinde TinyLlama modelini LoRA ile fine-tune etmek icin hazirlanmis ornek bir notebook icerir.

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
3. TinyLlama modelini 4-bit quantization ile yukler.
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
