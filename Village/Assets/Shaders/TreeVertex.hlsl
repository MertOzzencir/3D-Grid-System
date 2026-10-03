#ifndef VILLAGE_TREE_VERTEX_INCLUDED
#define VILLAGE_TREE_VERTEX_INCLUDED

// Ağacın vertex deformasyonu (TreeChopLit): balta göçüğü (ChopDent) + rüzgâr (Wind)
#include "ChopDent.hlsl"
#include "Wind.hlsl"

void ApplyTreeVertex(inout float3 positionOS)
{
    ApplyChopDent(positionOS);
    ApplyWind(positionOS);
}

void ApplyTreeVertex(inout float3 positionOS, inout float3 normalOS)
{
    ApplyChopDent(positionOS, normalOS);
    ApplyWind(positionOS);
}

#endif
