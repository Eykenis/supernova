#ifndef SUPERNOVA_SOFT_FALLOFF_ATTENUATION_INCLUDED
#define SUPERNOVA_SOFT_FALLOFF_ATTENUATION_INCLUDED

// Shared by every Supernova material that softens punctual attenuation, so cave
// walls and cave vegetation stay on one curve. Declared global rather than
// per-material to keep including shaders SRP Batcher compatible.
// x: attenuation exponent, y: near attenuation cap, z: multiplier.
float4 _SupernovaSoftFalloffParams;

half SoftenedPunctualAttenuation(half attenuation)
{
    float falloffPower = _SupernovaSoftFalloffParams.x > 0.0
        ? _SupernovaSoftFalloffParams.x
        : 1.0;
    float attenuationLimit = _SupernovaSoftFalloffParams.y > 0.0
        ? _SupernovaSoftFalloffParams.y
        : HALF_MAX;
    float lightMultiplier = _SupernovaSoftFalloffParams.z > 0.0
        ? _SupernovaSoftFalloffParams.z
        : 1.0;

    float safeAttenuation = max((float)attenuation, 0.0);
    float softened = pow(safeAttenuation, falloffPower) * lightMultiplier;
    return (half)min(softened, attenuationLimit);
}

#endif
