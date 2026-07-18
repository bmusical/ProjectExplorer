import os
from PIL import Image, ImageDraw, ImageFilter, ImageOps

# 1. Setup Canvas (1280x720 at 72 DPI)
CANVAS_WIDTH = 1280
CANVAS_HEIGHT = 720
canvas = Image.new("RGBA", (CANVAS_WIDTH, CANVAS_HEIGHT), (15, 18, 26, 255)) # Deeper, richer dark background

# 2. Draw an Intense "Neon Blue" Accent Glow
glow_layer = Image.new("RGBA", (CANVAS_WIDTH, CANVAS_HEIGHT), (0, 0, 0, 0))
glow_draw = ImageDraw.Draw(glow_layer)
# Brightened Brand Blue (#1a5cd6) for maximum impact
glow_draw.ellipse([200, 50, 1000, 670], fill=(26, 92, 214, 75)) 
glow_layer = glow_layer.filter(ImageFilter.GaussianBlur(90)) 
canvas = Image.alpha_composite(canvas, glow_layer)

def create_card_with_shadow(img_path, crop_box, border_color=(70, 85, 110, 255)):
    """Crops a snippet, adds a clean border, and applies a deep drop shadow."""
    if not os.path.exists(img_path):
        return None
    
    cropped = Image.open(img_path).crop(crop_box).convert("RGBA")
    bordered = ImageOps.expand(cropped, border=1, fill=border_color)
    
    # Create a deep, crisp drop shadow for 3D depth
    shadow_margin = 20
    shadow = Image.new("RGBA", (bordered.width + shadow_margin * 2, bordered.height + shadow_margin * 2), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    s_draw.rectangle([shadow_margin + 4, shadow_margin + 8, shadow_margin + bordered.width + 4, shadow_margin + bordered.height + 8], fill=(0, 0, 0, 140))
    shadow = shadow.filter(ImageFilter.GaussianBlur(12))
    
    shadow.paste(bordered, (shadow_margin, shadow_margin), bordered)
    return shadow

# 3. BASE LAYER: HxM Dev Hook (Top Left Background - Soft Blurred)
card_hxm = create_card_with_shadow("image_3d7f46.png", (0, 110, 260, 360))
if card_hxm:
    # Applying a slight blur to background elements for "focus depth"
    card_hxm_blurred = card_hxm.filter(ImageFilter.GaussianBlur(2))
    canvas.paste(card_hxm_blurred, (40, 40), card_hxm_blurred)

# 4. MIDDLE LAYER: Main Centerpiece Window (Shifted Left to clear space)
if os.path.exists("image_3d73a6.png"):
    main_window = Image.open("image_3d73a6.png").convert("RGBA")
    main_window.thumbnail((800, 480)) # Crisp scaling
    
    main_bordered = ImageOps.expand(main_window, border=1, fill=(80, 100, 130, 255))
    shadow_m = 25
    main_shadow = Image.new("RGBA", (main_bordered.width + shadow_m*2, main_bordered.height + shadow_m*2), (0, 0, 0, 0))
    ms_draw = ImageDraw.Draw(main_shadow)
    ms_draw.rectangle([shadow_m, shadow_m + 5, shadow_m + main_bordered.width, shadow_m + main_bordered.height + 5], fill=(0, 0, 0, 160))
    main_shadow = main_shadow.filter(ImageFilter.GaussianBlur(20))
    main_shadow.paste(main_bordered, (shadow_m, shadow_m), main_bordered)
    
    # Pushed Left to x=100 (Instead of being perfectly centered)
    canvas.paste(main_shadow, (100, 110), main_shadow)

# 5. FOREGROUND LAYER 1: Music Project Hook (Bottom Left Foreground - Sharp)
card_music = create_card_with_shadow("image_3d8272.png", (0, 0, 260, 160))
if card_music:
    canvas.paste(card_music, (50, 480), card_music)

# 6. FOREGROUND LAYER 2: The Genealogy Spotlight (Top Right OVERLAY - Completely Visible!)
# Increased the crop box height to capture the entire "Ancestors of note" and "Castles" tree
card_genealogy = create_card_with_shadow("image_3d8a2d.png", (0, 50, 340, 440))
if card_genealogy:
    # Placed on the right, overlapping the main window to look incredibly dynamic
    canvas.paste(card_genealogy, (880, 140), card_genealogy)

# 7. Save Output
canvas.save("gumroad_cover_final.png", "PNG")
print("Success! Upgraded 'gumroad_cover_final.png' has been created successfully.")