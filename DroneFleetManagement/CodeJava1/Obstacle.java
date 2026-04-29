/*
// 2D Drawing, survole
import java.awt.Color;
import java.awt.Font;
import java.awt.Graphics2D;
import java.util.Random;

public class Obstacle {
    public double x, y, width, height, radius;
    public double hauteurMeters; 
    public boolean isCircle;
    private static Random rnd = new Random();

    public Obstacle() {
        this.x = 220 + rnd.nextInt(350);
        this.y = 170 + rnd.nextInt(200);
        this.hauteurMeters = 50 + rnd.nextInt(40); // Max 90m
        this.isCircle = rnd.nextBoolean();
        if (isCircle) {
            this.radius = 20 + rnd.nextInt(20);
        } else {
            this.width = 40 + rnd.nextInt(40);
            this.height = 40 + rnd.nextInt(40);
        }
    }

    public void draw(Graphics2D g2) {
        g2.setColor(new Color(60, 60, 60, 180));
        if (isCircle) g2.fillOval((int)(x - radius), (int)(y - radius), (int)radius*2, (int)radius*2);
        else g2.fillRect((int)x, (int)y, (int)width, (int)height);
        
        g2.setColor(Color.BLACK);
        if (isCircle) g2.drawOval((int)(x - radius), (int)(y - radius), (int)radius*2, (int)radius*2);
        else g2.drawRect((int)x, (int)y, (int)width, (int)height);
        
        g2.setFont(new Font("Arial", Font.BOLD, 10));
        g2.drawString((int)hauteurMeters + "m", (int)x - 5, (int)y - 5);
    }
}*/

// 2D Drawing, Contour
import java.awt.Color;
import java.awt.Graphics2D;
import java.util.Random;
public class Obstacle {
    public double x, y, width, height, radius;
    public boolean isCircle, scanComplet = false;
    private static Random rnd = new Random();
    public Obstacle() {
        this.x = 220 + rnd.nextInt(350);
        this.y = 170 + rnd.nextInt(200);
        this.isCircle = rnd.nextBoolean();
        if (isCircle) {
            this.radius = 20 + rnd.nextInt(5);  // rayon du cercle
        }
        else {
            this.width = 40 + rnd.nextInt(20); //largeur du rectangle
            this.height = 40 + rnd.nextInt(20); //hauteur du rectangle
        }
    }
    public void draw(Graphics2D g2) {
        g2.setColor(scanComplet ? new Color(100, 100, 100, 150) : new Color(220, 50, 50, 200));
        if (isCircle) g2.fillOval((int)(x - radius), (int)(y - radius), (int)radius*2, (int)radius*2);
        else g2.fillRect((int)x, (int)y, (int)width, (int)height);
        g2.setColor(Color.BLACK);
        if (isCircle) g2.drawOval((int)(x - radius), (int)(y - radius), (int)radius*2, (int)radius*2);
        else g2.drawRect((int)x, (int)y, (int)width, (int)height);
    }
}

