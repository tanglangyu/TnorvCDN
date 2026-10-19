package pipeline

// s3origin.go S3 对象存储源站（AWS SigV4）：scheme=s3 的源站按桶对象语义回源。
// URL 映射：/path → GET https://endpoint/bucket/path（path-style，兼容 MinIO/OSS/COS/七牛）。
// 签名：SigV4 UNSIGNED-PAYLOAD，查询串不参与签名（GET 无 body）。

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"net/http"
	"sort"
	"strings"
	"time"

	"tnorv/node/pb"
)

const s3UnsignedPayload = "UNSIGNED-PAYLOAD"

// isS3Origin 判断是否对象存储源站
func isS3Origin(o *pb.Origin) bool { return o != nil && o.Scheme == "s3" && o.Bucket != "" }

// s3Sign 为请求附加 SigV4 头（path-style；req.URL 已指向 endpoint/bucket/key）
func s3Sign(req *http.Request, o *pb.Origin) {
	region := o.Region
	if region == "" {
		region = "us-east-1"
	}
	now := time.Now().UTC()
	amzDate := now.Format("20060102T150405Z")
	dateStamp := now.Format("20060102")

	// canonical URI：路径已编码
	canonicalURI := req.URL.EscapedPath()
	if canonicalURI == "" {
		canonicalURI = "/"
	}
	// canonical query：按 key 排序
	var qkeys []string
	qv := req.URL.Query()
	for k := range qv {
		qkeys = append(qkeys, k)
	}
	sort.Strings(qkeys)
	var qparts []string
	for _, k := range qkeys {
		for _, v := range qv[k] {
			qparts = append(qparts, uriEncode(k)+"="+uriEncode(v))
		}
	}
	canonicalQuery := strings.Join(qparts, "&")

	host := req.Host
	if host == "" {
		host = req.URL.Host
	}
	canonicalHeaders := "host:" + host + "\nx-amz-content-sha256:" + s3UnsignedPayload + "\nx-amz-date:" + amzDate + "\n"
	signedHeaders := "host;x-amz-content-sha256;x-amz-date"

	canonicalRequest := strings.Join([]string{req.Method, canonicalURI, canonicalQuery, canonicalHeaders, signedHeaders, s3UnsignedPayload}, "\n")
	scope := dateStamp + "/" + region + "/s3/aws4_request"
	ss := strings.Join([]string{"AWS4-HMAC-SHA256", amzDate, scope, sha256hex(canonicalRequest)}, "\n")

	kDate := hmacSHA256([]byte("AWS4"+o.SecretKey), []byte(dateStamp))
	kRegion := hmacSHA256(kDate, []byte(region))
	kService := hmacSHA256(kRegion, []byte("s3"))
	kSigning := hmacSHA256(kService, []byte("aws4_request"))
	signature := hex.EncodeToString(hmacSHA256(kSigning, []byte(ss)))

	req.Header.Set("Authorization", fmt.Sprintf("AWS4-HMAC-SHA256 Credential=%s/%s, SignedHeaders=%s, Signature=%s",
		o.AccessKey, scope, signedHeaders, signature))
	req.Header.Set("x-amz-date", amzDate)
	req.Header.Set("x-amz-content-sha256", s3UnsignedPayload)
}

func hmacSHA256(key, data []byte) []byte {
	h := hmac.New(sha256.New, key)
	h.Write(data)
	return h.Sum(nil)
}

func sha256hex(s string) string {
	sum := sha256.Sum256([]byte(s))
	return hex.EncodeToString(sum[:])
}

// uriEncode AWS URI 编码（保留 -_.~，其余转 %XX 大写）
func uriEncode(s string) string {
	var b strings.Builder
	for i := 0; i < len(s); i++ {
		c := s[i]
		if (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~' {
			b.WriteByte(c)
		} else {
			fmt.Fprintf(&b, "%%%02X", c)
		}
	}
	return b.String()
}
