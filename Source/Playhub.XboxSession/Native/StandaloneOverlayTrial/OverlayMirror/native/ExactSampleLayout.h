// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <cstdint>
#include <algorithm>
namespace playhub::mirror::output {
constexpr unsigned GridColumns=64,GridRows=36;
enum class ImageLayout : std::uint32_t {Full,SourceRows,OutputRows};
struct SampleGrid {std::uint32_t sourceWidth{},sourceHeight{};};
inline bool ValidGrid(SampleGrid grid)noexcept{return grid.sourceWidth>=32&&grid.sourceHeight>=32&&grid.sourceWidth<=16384&&grid.sourceHeight<=16384;}
inline unsigned GridX(unsigned width,unsigned column)noexcept{return 1+unsigned((std::uint64_t(column)*2+1)*(width-3)/128);}
inline unsigned GridY(unsigned height,unsigned row)noexcept{return 1+unsigned((std::uint64_t(row)*2+1)*(height-3)/72);}
inline unsigned OutputGridY(unsigned sourceHeight,unsigned outputHeight,unsigned row)noexcept{const auto y=GridY(sourceHeight,row);return std::min(outputHeight-1,unsigned((std::uint64_t(y)*2+1)*outputHeight/(std::uint64_t(sourceHeight)*2)));}
inline unsigned StoredRows(ImageLayout layout,unsigned logicalHeight)noexcept{return layout==ImageLayout::Full?logicalHeight:layout==ImageLayout::SourceRows?GridRows*3:layout==ImageLayout::OutputRows?GridRows:0;}
}
