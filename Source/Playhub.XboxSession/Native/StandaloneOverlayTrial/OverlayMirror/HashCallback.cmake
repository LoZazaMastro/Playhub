file(SHA256 "${DLL}" digest)
string(TOUPPER "${digest}" digest)
file(WRITE "${HEADER}" "#pragma once\ninline constexpr wchar_t ExpectedExactBandSha[]=L\"${digest}\";\n")
