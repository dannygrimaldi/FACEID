namespace Face.Api.Core.FacePipeline.Recognition
{
    public static class EmbeddingUtils
    {
        public static float[] L2Normalize(float[] v)
        {
            float sum = 0f;
            for (int i = 0; i < v.Length; i++)
                sum += v[i] * v[i];

            float norm = MathF.Sqrt(sum);

            for (int i = 0; i < v.Length; i++)
                v[i] /= norm;

            return v;
        }

        public static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0f;
            for (int i = 0; i < a.Length; i++)
                dot += a[i] * b[i];

            return dot;
        }
    }
}
