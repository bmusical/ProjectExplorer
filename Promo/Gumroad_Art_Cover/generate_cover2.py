import os
from PIL import Image, ImageDraw, ImageFilter

CANVAS_WIDTH = 1280
CANVAS_HEIGHT = 720

# Create a completely blank, transparent canvas layer [cite: 161]
vignette_layer = Image.new("RGBA", (CANVAS_WIDTH, CANVAS_HEIGHT), (0, 0, 0, 0))

def make_feathered_circle(image_path, size, feather_radius=25):
    """Crops an image into a circle and beautifully feathers the edges to transparent[cite: 168]."""
    if not os.path.exists(image_path):
        # Placeholder colored circle if you haven't saved your stock image yet
        img = Image.new("RGBA", (size, size), (26, 92, 214, 100))
    else:
        img = Image.open(image_path).convert("RGBA").resize((size, size), Image.Resampling.LANCZOS)
    
    # Create the circle mask [cite: 168]
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    pad = feather_radius
    draw.ellipse([pad, pad, size - pad, size - pad], fill=255)
    
    # Softly blur the mask to create the smooth transition gradient [cite: 168]
    mask = mask.filter(ImageFilter.GaussianBlur(feather_radius))
    
    # Apply mask to image [cite: 168]
    output = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    output.paste(img, (0, 0), mask)
    return output

# --- Generate the Mini Vignettes  ---

# 1. Music Open Mic Scene (New addition! Placed near bottom-left music branch)
circle_music = make_feathered_circle("music_scene.jpg", size=170, feather_radius=22)
vignette_layer.paste(circle_music, (40, 260), circle_music)

# 2. Revolutionary War Scene (Placed near Genealogy/History branch)
circle_rev = make_feathered_circle("revolutionary_war.jpg", size=180, feather_radius=20)
vignette_layer.paste(circle_rev, (1050, 480), circle_rev)

# 3. Titanic / Rev. Bateman Scene
circle_titanic = make_feathered_circle("titanic.jpg", size=150, feather_radius=18)
vignette_layer.paste(circle_titanic, (1080, 20), circle_titanic)

# 4. Castles Scene
circle_castle = make_feathered_circle("castle.jpg", size=200, feather_radius=25)
vignette_layer.paste(circle_castle, (740, 20), circle_castle)

# 5. Developer Shot (Placed near top-left code branch)
circle_dev = make_feathered_circle("developer.jpg", size=150, feather_radius=20)
vignette_layer.paste(circle_dev, (240, 20), circle_dev)

# Save the final transparent layer overlay [cite: 168]
vignette_layer.save("transparent_vignettes_layer.png", "PNG")
print("Success! Updated 'transparent_vignettes_layer.png' built with Music Scene included.")