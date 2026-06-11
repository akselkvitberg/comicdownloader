package main

import "testing"

func TestNormalize(t *testing.T) {
	cases := []struct{ in, want string }{
		{"Lunch TU", "lunchtu"},
		{"XKCD", "xkcd"},
		{"The Far Side", "thefarside"},
		{"Lunch E24", "lunche24"},
		{"Swords", "swords"},
		{"settings", "settings"},
	}
	for _, c := range cases {
		if got := normalize(c.in); got != c.want {
			t.Errorf("normalize(%q) = %q; want %q", c.in, got, c.want)
		}
	}
}

func TestObjectName(t *testing.T) {
	if got := objectName("XKCD", "abc123"); got != "xkcd/abc123" {
		t.Errorf("got %q", got)
	}
}

func TestSecretID(t *testing.T) {
	if got := secretID("comicdownloader-", "settings", "onedrive"); got != "comicdownloader-settings-onedrive" {
		t.Errorf("got %q", got)
	}
}
