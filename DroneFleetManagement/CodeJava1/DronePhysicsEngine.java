public class DronePhysicsEngine {
    private final double dt;
    // Limites du monde (Bordures)
    private final double minX = 0, maxX = 800;
    private final double minY = 0, maxY = 600;
    private final double minZ = -500, maxZ = 500;

    public DronePhysicsEngine(double timeStep) {
        this.dt = timeStep;
    }

    public DroneState computeNextState(DroneState current, double ax, double ay, double az) {
        DroneState next = new DroneState(current.id, current.x, current.y, current.z);

        // Intégration Euler
        next.vx = current.vx + ax * dt;
        next.vy = current.vy + ay * dt;
        //next.vz = current.vz + az * dt;

        next.x = current.x + next.vx * dt;
        next.y = current.y + next.vy * dt;
        //next.z = current.z + next.vz * dt;

        // --- Gestion des Bordures (Rebond) ---
        if (next.x < minX || next.x > maxX) { next.vx *= -0.5; next.x = (next.x < minX) ? minX : maxX; }
        if (next.y < minY || next.y > maxY) { next.vy *= -0.5; next.y = (next.y < minY) ? minY : maxY; }
        //if (next.z < minZ || next.z > maxZ) { next.vz *= -0.5; next.z = (next.z < minZ) ? minZ : maxZ; }

        return next;
    }
}