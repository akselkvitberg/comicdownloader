package main

import (
	"encoding/json"
	"encoding/xml"
	"fmt"
	"html"
	"io"
	"net/http"
	"regexp"
	"strings"
	"time"
)

type ComicResult struct {
	Data    []byte
	Caption *string
}

var (
	farSideRe   = regexp.MustCompile(`(?s)<div class="card tfs-comic js-comic">.*?<img[^>]*data-src="(https://featureassets\.amuniversal\.com/assets/[^"]+)".*?<figcaption class="figure-caption">(.*?)</figcaption>`)
	htmlTagRe   = regexp.MustCompile(`<[^>]*>`)
	rssImgURLRe = regexp.MustCompile(`img.*?src="(\S+)"`)
)

func buildTUURL(comic string) string {
	// Replicates F# interpolation: $"...?name{comic}&date=..." — no = between name and comic
	return "https://www.tu.no/api/widgets/comics?name" + comic + "&date=" + time.Now().Format("2006-01-02")
}

func buildE24URL() string {
	return "https://api.e24.no/content/v1/comics/" + time.Now().Format("2006-01-02")
}

func fetchURL(u string) ([]byte, error) {
	resp, err := http.Get(u)
	if err != nil {
		return nil, fmt.Errorf("GET %s: %w", u, err)
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 300 {
		return nil, fmt.Errorf("GET %s: status %d", u, resp.StatusCode)
	}
	return io.ReadAll(resp.Body)
}

func extractRSSImageURL(xmlStr string) (string, error) {
	type RSS struct {
		Channel struct {
			Item struct {
				Description string `xml:"description"`
			} `xml:"item"`
		} `xml:"channel"`
	}
	var rss RSS
	if err := xml.Unmarshal([]byte(xmlStr), &rss); err != nil {
		return "", fmt.Errorf("parse RSS: %w", err)
	}
	desc := html.UnescapeString(rss.Channel.Item.Description)
	m := rssImgURLRe.FindStringSubmatch(desc)
	if m == nil {
		return "", fmt.Errorf("no image URL in RSS description")
	}
	return m[1], nil
}

func extractFarSideComic(pageURL, htmlContent string) (string, *string, error) {
	matches := farSideRe.FindAllStringSubmatch(htmlContent, -1)
	if len(matches) == 0 {
		return "", nil, fmt.Errorf("no Far Side comic found on %s", pageURL)
	}
	for _, m := range matches {
		imgURL := m[1]
		rawCaption := strings.TrimSpace(html.UnescapeString(htmlTagRe.ReplaceAllString(m[2], "")))
		if rawCaption != "" {
			return imgURL, &rawCaption, nil
		}
	}
	return matches[0][1], nil, nil
}

func DownloadLunchTU() (*ComicResult, error) {
	data, err := fetchURL(buildTUURL("lunch"))
	if err != nil {
		return nil, err
	}
	return &ComicResult{Data: data}, nil
}

func DownloadLunchE24() (*ComicResult, error) {
	data, err := fetchURL(buildE24URL())
	if err != nil {
		return nil, err
	}
	return &ComicResult{Data: data}, nil
}

func DownloadXKCD() (*ComicResult, error) {
	resp, err := http.Get("https://xkcd.com/info.0.json")
	if err != nil {
		return nil, fmt.Errorf("XKCD JSON: %w", err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	var xkcd struct {
		Img string `json:"img"`
		Alt string `json:"alt"`
	}
	if err := json.Unmarshal(body, &xkcd); err != nil {
		return nil, fmt.Errorf("parse XKCD JSON: %w", err)
	}
	data, err := fetchURL(xkcd.Img)
	if err != nil {
		return nil, err
	}
	return &ComicResult{Data: data, Caption: &xkcd.Alt}, nil
}

func DownloadFarSide() (*ComicResult, error) {
	pageURL := time.Now().Format("https://www.thefarside.com/2006/01/02")
	req, err := http.NewRequest("GET", pageURL, nil)
	if err != nil {
		return nil, fmt.Errorf("build Far Side request: %w", err)
	}
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("GET Far Side: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 300 {
		return nil, fmt.Errorf("GET Far Side: status %d", resp.StatusCode)
	}
	htmlBytes, _ := io.ReadAll(resp.Body)
	imgURL, caption, err := extractFarSideComic(pageURL, string(htmlBytes))
	if err != nil {
		return nil, err
	}
	data, err := fetchURL(imgURL)
	if err != nil {
		return nil, err
	}
	return &ComicResult{Data: data, Caption: caption}, nil
}

func DownloadRSS(feedURL string) (*ComicResult, error) {
	resp, err := http.Get(feedURL)
	if err != nil {
		return nil, fmt.Errorf("GET RSS: %w", err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	imgURL, err := extractRSSImageURL(string(body))
	if err != nil {
		return nil, err
	}
	data, err := fetchURL(imgURL)
	if err != nil {
		return nil, err
	}
	return &ComicResult{Data: data}, nil
}
