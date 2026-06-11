package main

import (
	"strings"
	"testing"
	"time"
)

func TestBuildTUURL(t *testing.T) {
	today := time.Now().Format("2006-01-02")
	got := buildTUURL("lunch")
	// Replicates F# behavior: ?namelunch (no = between "name" and comic value)
	want := "https://www.tu.no/api/widgets/comics?namelunch&date=" + today
	if got != want {
		t.Errorf("got %q; want %q", got, want)
	}
}

func TestBuildE24URL(t *testing.T) {
	today := time.Now().Format("2006-01-02")
	got := buildE24URL()
	want := "https://api.e24.no/content/v1/comics/" + today
	if got != want {
		t.Errorf("got %q; want %q", got, want)
	}
}

func TestExtractRSSImageURL(t *testing.T) {
	xmlInput := `<rss><channel><item><description>&lt;img src="https://example.com/comic.jpg" /&gt;</description></item></channel></rss>`
	got, err := extractRSSImageURL(xmlInput)
	if err != nil {
		t.Fatalf("extractRSSImageURL: %v", err)
	}
	if got != "https://example.com/comic.jpg" {
		t.Errorf("got %q", got)
	}
}

func TestExtractFarSideComic(t *testing.T) {
	htmlContent := `<div class="card tfs-comic js-comic"><img data-src="https://featureassets.amuniversal.com/assets/img.jpg" /><figcaption class="figure-caption">Funny caption</figcaption></div>`
	imgURL, caption, err := extractFarSideComic("https://www.thefarside.com/2024/01/01", htmlContent)
	if err != nil {
		t.Fatalf("extractFarSideComic: %v", err)
	}
	if imgURL != "https://featureassets.amuniversal.com/assets/img.jpg" {
		t.Errorf("imgURL = %q", imgURL)
	}
	if caption == nil || !strings.Contains(*caption, "Funny caption") {
		t.Errorf("caption = %v", caption)
	}
}

func TestExtractFarSideFallback(t *testing.T) {
	htmlContent := `<div class="card tfs-comic js-comic"><img data-src="https://featureassets.amuniversal.com/assets/img.jpg" /><figcaption class="figure-caption">   </figcaption></div>`
	imgURL, caption, err := extractFarSideComic("https://www.thefarside.com/2024/01/01", htmlContent)
	if err != nil {
		t.Fatalf("extractFarSideComic: %v", err)
	}
	if imgURL != "https://featureassets.amuniversal.com/assets/img.jpg" {
		t.Errorf("imgURL = %q", imgURL)
	}
	if caption != nil {
		t.Errorf("expected nil caption, got %q", *caption)
	}
}
