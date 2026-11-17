// SPDX-License-Identifier: MIT
#pragma once
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <optional>

namespace content {
constexpr std::int64_t FreshTicks=2000000; // 200ms, QPC times expressed in 100ns.
struct Rect {int x{},y{},width{},height{};bool operator==(const Rect&)const=default;};
struct Stats {unsigned samples{},opaque{},nonBlack{};double mean{},variance{};unsigned minimum{255},maximum{};};
inline bool Fresh(std::int64_t frame,std::int64_t now){return frame>0&&now>=frame&&now-frame<=FreshTicks;}
inline bool Bounds(unsigned width,unsigned height,Rect crop){
 return width>0&&height>0&&width<=8192&&height<=8192&&static_cast<std::uint64_t>(width)*height<=16777216
  &&crop.x>=0&&crop.y>=0&&crop.width>0&&crop.height>0
  &&static_cast<unsigned>(crop.x)<=width&&static_cast<unsigned>(crop.y)<=height
  &&static_cast<unsigned>(crop.width)<=width-static_cast<unsigned>(crop.x)
  &&static_cast<unsigned>(crop.height)<=height-static_cast<unsigned>(crop.y);
}
inline std::optional<Stats> Measure(const std::uint8_t* bytes,std::size_t length,std::size_t pitch,unsigned width,unsigned height){
 if(!bytes||!Bounds(width,height,{0,0,static_cast<int>(width),static_cast<int>(height)})
  ||pitch<static_cast<std::size_t>(width)*4||pitch>std::numeric_limits<std::size_t>::max()/height||length<pitch*height)return std::nullopt;
 Stats s;std::uint64_t sum=0,squares=0;
 const unsigned columns=std::min(32u,width),rows=std::min(18u,height);
 for(unsigned y=0;y<rows;y++)for(unsigned x=0;x<columns;x++){
  const unsigned px=static_cast<unsigned>((static_cast<std::uint64_t>(x)*2+1)*width/(columns*2));
  const unsigned py=static_cast<unsigned>((static_cast<std::uint64_t>(y)*2+1)*height/(rows*2));
  const auto p=bytes+pitch*py+static_cast<std::size_t>(px)*4;
  // BGRA8 integer luma, no RGB/pixel arrays leave this function.
  const unsigned luma=(54u*p[2]+183u*p[1]+19u*p[0]+128u)>>8;
  ++s.samples;if(p[3]>=240)++s.opaque;if(p[3]>=240&&luma>8)++s.nonBlack;
  s.minimum=std::min(s.minimum,luma);s.maximum=std::max(s.maximum,luma);sum+=luma;squares+=luma*luma;
 }
 s.mean=static_cast<double>(sum)/s.samples;
 s.variance=std::max(0.0,static_cast<double>(squares)/s.samples-s.mean*s.mean);
 return s;
}
}
