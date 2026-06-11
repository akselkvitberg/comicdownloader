package main

import (
	"strings"
	"testing"
)

func TestNormalizeCaption(t *testing.T) {
	if got := normalizeCaption(""); got != nil {
		t.Errorf("empty: want nil, got %q", *got)
	}
	if got := normalizeCaption("   "); got != nil {
		t.Errorf("whitespace: want nil, got %q", *got)
	}
	s := "Hello world"
	if got := normalizeCaption(s); got == nil || *got != s {
		t.Errorf("short: got %v", got)
	}
	long := strings.Repeat("a", 2000)
	got := normalizeCaption(long)
	if got == nil || len(*got) != 1024 {
		t.Errorf("long: want len 1024, got %v", got)
	}
}
