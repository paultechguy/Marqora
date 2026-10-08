// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// A picture of one equation, drawn by the preview at twice its size, for an equation the
/// Word export cannot write as a Word equation.
/// </summary>
/// <param name="Png">The picture.</param>
/// <param name="DepthPixels">
/// How far the picture reaches below the equation's baseline, in CSS pixels at its natural
/// size. Word stands an inline picture on the baseline, so an equation in a line of text is
/// lowered by this much to sit on the line as it does on screen. Zero for a displayed one.
/// </param>
public sealed record MathPicture(byte[] Png, double DepthPixels);
