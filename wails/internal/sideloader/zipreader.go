package sideloader

import (
	"bytes"
	"compress/flate"
	"encoding/binary"
	"fmt"
	"io"
	"strings"
)

const (
	eocdSignature            = 0x06054b50
	centralDirSignature      = 0x02014b50
	localFileHeaderSignature = 0x04034b50
	compressionStore         = 0
	compressionDeflate       = 8
)

type fileEntry struct {
	name              string
	compressedSize    uint32
	uncompressedSize  uint32
	offset            uint32
	compressionMethod uint16
}

// readCentralDir 解析中央目录，返回 nil 表示数据不完整或出错
func readCentralDir(data []byte, totalSize int64) map[string]fileEntry {
	if int64(len(data)) > totalSize {
		return nil
	}
	eocdOffset := findEOCD(data)
	if eocdOffset < 0 {
		return nil
	}

	cdEntries := binary.LittleEndian.Uint16(data[eocdOffset+8 : eocdOffset+10])
	cdSize := binary.LittleEndian.Uint32(data[eocdOffset+12 : eocdOffset+16])
	cdOffset := binary.LittleEndian.Uint32(data[eocdOffset+16 : eocdOffset+20])

	if cdOffset == 0xFFFFFFFF {
		return nil // ZIP64 not supported
	}

	dataStartOffset := totalSize - int64(len(data))
	cdRelativeOffset := int64(cdOffset) - dataStartOffset

	if cdRelativeOffset < 0 || cdRelativeOffset+int64(cdSize) > int64(len(data)) {
		return nil
	}

	entries := make(map[string]fileEntry)
	cdData := data[cdRelativeOffset:]
	offset := 0

	for i := uint16(0); i < cdEntries; i++ {
		if offset+46 > len(cdData) {
			break
		}
		if binary.LittleEndian.Uint32(cdData[offset:offset+4]) != centralDirSignature {
			break
		}

		entry := fileEntry{
			compressionMethod: binary.LittleEndian.Uint16(cdData[offset+10 : offset+12]),
			compressedSize:    binary.LittleEndian.Uint32(cdData[offset+20 : offset+24]),
			uncompressedSize:  binary.LittleEndian.Uint32(cdData[offset+24 : offset+28]),
			offset:            binary.LittleEndian.Uint32(cdData[offset+42 : offset+46]),
		}

		nameLen := binary.LittleEndian.Uint16(cdData[offset+28 : offset+30])
		extraLen := binary.LittleEndian.Uint16(cdData[offset+30 : offset+32])
		commentLen := binary.LittleEndian.Uint16(cdData[offset+32 : offset+34])
		totalLen := 46 + int(nameLen) + int(extraLen) + int(commentLen)

		if offset+totalLen > len(cdData) {
			break
		}

		entry.name = string(cdData[offset+46 : offset+46+int(nameLen)])
		entries[entry.name] = entry
		offset += totalLen
	}

	return entries
}

func findEOCD(data []byte) int {
	if len(data) < 22 {
		return -1
	}
	searchStart := len(data) - 22
	for i := searchStart; i >= 0; i-- {
		if binary.LittleEndian.Uint32(data[i:i+4]) == eocdSignature {
			commentLen := binary.LittleEndian.Uint16(data[i+20 : i+22])
			if int64(i)+22+int64(commentLen) == int64(len(data)) {
				return i
			}
		}
	}
	return -1
}

func parseLocalHeader(data []byte) (int64, uint32, error) {
	if len(data) < 30 {
		return 0, 0, fmt.Errorf("insufficient data")
	}
	if binary.LittleEndian.Uint32(data[0:4]) != localFileHeaderSignature {
		return 0, 0, fmt.Errorf("invalid signature")
	}
	compressedSize := binary.LittleEndian.Uint32(data[18:22])
	nameLen := binary.LittleEndian.Uint16(data[26:28])
	extraLen := binary.LittleEndian.Uint16(data[28:30])
	return int64(30 + nameLen + extraLen), compressedSize, nil
}

func extractManifestXML(data []byte, compressionMethod uint16) (string, error) {
	var xmlData []byte
	var err error

	switch compressionMethod {
	case compressionStore:
		xmlData = data
	case compressionDeflate:
		reader := flate.NewReader(bytes.NewReader(data))
		defer reader.Close()
		xmlData, err = io.ReadAll(reader)
		if err != nil {
			return "", err
		}
	default:
		return "", fmt.Errorf("unsupported compression: %d", compressionMethod)
	}

	startIdx := strings.Index(string(xmlData), "<guid>")
	if startIdx == -1 {
		return "", fmt.Errorf("guid not found")
	}
	endIdx := strings.Index(string(xmlData[startIdx+6:]), "</guid>")
	if endIdx == -1 {
		return "", fmt.Errorf("guid end not found")
	}
	return string(xmlData[startIdx+6 : startIdx+6+endIdx]), nil
}
