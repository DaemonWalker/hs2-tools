package main

import (
	"encoding/json"
	"fmt"
	"os"

	"wails/internal/sideloader"
)

func main() {
	var proxy string
	for i := 1; i < len(os.Args); i++ {
		if os.Args[i] == "-proxy" && i+1 < len(os.Args) {
			proxy = os.Args[i+1]
			i++
		}
	}

	sl := sideloader.NewSideloader(proxy)

	result, err := sl.Run(
		func(msg string) {
			fmt.Printf("[Sideloader] %s\n", msg)
		},
		func(current, total int) {
			if total > 0 {
				fmt.Printf("[Sideloader] Progress: %d/%d\n", current, total)
			} else {
				fmt.Printf("[Sideloader] Found: %d\n", current)
			}
		},
	)

	if err != nil {
		fmt.Printf("[Sideloader] Error: %v\n", err)
		os.Exit(1)
	}

	file, err := os.Create("result.json")
	if err != nil {
		fmt.Printf("[Sideloader] Failed to create result.json: %v\n", err)
		os.Exit(1)
	}
	defer file.Close()
	encoder := json.NewEncoder(file)
	encoder.SetIndent("", "  ")
	if err := encoder.Encode(result); err != nil {
		fmt.Printf("[Sideloader] Failed to encode result: %v\n", err)
		os.Exit(1)
	}
	fmt.Printf("[Sideloader] Saved to result.json, found %d mods\n", len(result))
}
